## Context

`Expenses.Api` and `Expenses.Mcp` are two independent host processes that both call
`AddExpensesInfrastructure(configuration)` at startup and must not otherwise diverge in how they are
wired (D1). Today both rely on the default `Microsoft.Extensions.Logging` console provider:
`Expenses.Api` takes ASP.NET Core's default, and `Expenses.Mcp` explicitly adds a console provider
pinned to stderr for its stdio transport, because stdout there is reserved for MCP protocol frames
(`Expenses.Mcp/Program.cs`). Neither host persists logs anywhere. See proposal.md - Why.

## Goals / Non-Goals

**Goals:**
- One shared place (`Expenses.Infrastructure`) that both hosts call to get the same Serilog setup,
  so they cannot drift.
- Preserve the stdio transport's stdout purity guarantee exactly as it exists today.
- Durable, rotating file logs with bounded retention, requiring no manual cleanup.
- No new configuration schema — reuse the `Logging` section already present in both hosts'
  `appsettings*.json`.

**Non-Goals:**
- No log aggregation / shipping to an external sink (e.g. Seq, ELK, OpenTelemetry export). File +
  console only, for now.
- No structured request-logging middleware or enrichers beyond what Serilog provides out of the box
  (timestamp, level, source context). Correlation IDs, request-scoped enrichment, etc. are out of
  scope for this change.
- No change to what individual call sites log — this change swaps the logging provider/pipeline,
  not the log statements themselves.

## Decisions

**D28 — Shared setup lives in `Expenses.Infrastructure`, not a new project.**
A dedicated `Expenses.Logging` project was considered, but the composition rule (D1: no rule lives
in an adapter; a concern both hosts need lives one ring in) already gives `Infrastructure` this job
for DI wiring, and logging setup is the same kind of startup composition. One static class,
`ExpensesLoggingExtensions.AddExpensesLogging(this IHostBuilder/WebApplicationBuilder builder, ...)`,
configures Serilog via `UseSerilog(...)`. Avoids a fifth production project for a few dozen lines of
setup.

**D29 — Host-specific parameter for the console sink's stream, not two code paths.**
`Expenses.Api` and the MCP `http` transport want stdout; MCP `stdio` wants stderr. Rather than
branching internally on transport type (which would leak MCP concepts into `Infrastructure`), the
shared helper takes an explicit `bool useStandardError` (or equivalent) parameter, and each
`Program.cs` passes the value appropriate to its own startup path — the same shape as the existing
`AddConsole(options => options.LogToStandardErrorThreshold = ...)` call it replaces.

**D30 — Serilog configured via `ReadFrom.Configuration`, not a parallel C# config surface.**
`Serilog.Settings.Configuration` maps the existing `Logging:LogLevel:*` keys Serilog already
understands via its Microsoft.Extensions.Logging compatibility, so `appsettings.json` needs no new
section. Sinks (console, file) and their formatting/rotation options are still set in code in the
shared helper, since they are identical across environments and hosts and don't need to vary by
config.

**D31 — File sink: daily rolling file, 31-day retention, under `logs/` at each host's content
root.**
`Serilog.Sinks.File` supports rolling-by-day and a `retainedFileCountLimit` natively, so no separate
cleanup job is needed. `logs/` sits next to each host's `appsettings.json` (its content root),
keeping it host-scoped rather than shared — the two hosts are separate processes and could run on
different machines. Each host's `.gitignore` gets a `logs/` entry (or the repo root's, if simpler in
practice at task time).

**D32 — Fatal-on-startup and flush-on-exit wrapping.**
Both `Program.cs` files wrap their `app.Run()` / `await host.RunAsync()` call in
`try { ... } catch (Exception ex) { Log.Fatal(ex, "..."); throw; } finally { Log.CloseAndFlush(); }`
(or Serilog's `TryCreateBootstrapLogger` early-logging pattern) — the standard Serilog ASP.NET Core
pattern — so a crash during startup or the run loop is captured on disk before the process exits.

## Risks / Trade-offs

- [Serilog's Console sink writes to `Console.Out` by default; misconfiguring the stdio host's sink
  to stdout instead of stderr would silently corrupt the MCP protocol stream] → The stdio path is
  covered by an integration test that asserts nothing but valid MCP frames appears on stdout with
  logging at `Trace` level (mirrors the existing stdio transport tests, if any exist, or is added
  here).
- [Unbounded log file growth if retention cleanup fails to run, e.g., the process never restarts for
  weeks] → `retainedFileCountLimit` is enforced by the file sink on every roll, not only at startup,
  so this is bounded by Serilog itself rather than a separate job.
- [Two separate `Program.cs` call sites for the shared helper could still drift in the parameters
  they pass] → Kept to a single boolean parameter (D29) and covered by a task to add/update the
  reference-rule-style test that already guards the two hosts' composition symmetry, if one exists,
  or a simple assertion test otherwise.
