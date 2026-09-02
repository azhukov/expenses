## Why

Both hosts (`Expenses.Api`, `Expenses.Mcp`) currently rely on the default Microsoft.Extensions.Logging
console provider with no durable output. There is no record of what happened once a console window
closes, and the MCP stdio host in particular has a hard constraint (stdout is protocol-only) that the
default provider does not enforce for anyone extending it. Serilog gives structured, leveled logging
with a console sink for local development and a rolling file sink for a durable trail, wired the same
way in both hosts so they cannot drift.

## What Changes

- Add Serilog (`Serilog.AspNetCore` for `Expenses.Api`, `Serilog.Extensions.Hosting` for
  `Expenses.Mcp`, plus `Serilog.Sinks.Console` and `Serilog.Sinks.File`) as the logging provider for
  both hosts, replacing the default Microsoft console provider.
- Console sink: human-readable output for local development.
  - `Expenses.Api` and the MCP `http` transport write the console sink to stdout.
  - The MCP `stdio` transport keeps writing the console sink to stderr only, preserving the existing
    rule that stdout carries protocol traffic exclusively.
- File sink: writes structured, daily-rolling log files under a `logs/` directory relative to the
  host's content root, retained for 31 days, shared by both hosts.
- Logging configuration (minimum levels, level overrides) is read from each host's existing
  `appsettings.json` / `appsettings.Development.json` `Logging` section, so no new configuration
  surface is introduced beyond what Serilog's configuration provider already understands.
- Composition is shared: one `AddExpensesLogging` helper in `Expenses.Infrastructure` configures the
  Serilog logger for both hosts, so `Expenses.Api/Program.cs` and `Expenses.Mcp/Program.cs` call the
  same setup instead of each hand-rolling it.
- Startup and unhandled-exception logging: both hosts log a startup line and wrap `app.Run()` /
  `host.RunAsync()` so an unhandled exception is logged before the process exits, then Serilog is
  flushed on shutdown.

## Capabilities

### New Capabilities
- `observability`: how the two hosts produce and persist logs — sinks, destinations (stdout vs
  stderr vs file), rolling/retention policy, and the stdio-safety guarantee.

### Modified Capabilities
(none — no existing spec describes logging behavior today)

## Impact

- `BE/Expenses.Infrastructure`: new `AddExpensesLogging` composition helper and new
  `PackageReference`s (`Serilog.Extensions.Hosting`, `Serilog.Sinks.Console`, `Serilog.Sinks.File`,
  `Serilog.Settings.Configuration`).
- `BE/Expenses.Api/Program.cs`: replaces default logging with the shared Serilog setup;
  `Expenses.Api.csproj` gains `Serilog.AspNetCore`.
- `BE/Expenses.Mcp/Program.cs`: replaces `builder.Logging.AddConsole(...)` with the shared Serilog
  setup for both the `stdio` and `http` branches; `Expenses.Mcp.csproj` gains
  `Serilog.Extensions.Hosting`.
- `appsettings.json` / `appsettings.Development.json` in both hosts: no schema change required
  (Serilog reads the existing `Logging:LogLevel` shape via `Serilog.Settings.Configuration`), but a
  `.gitignore` entry for the new `logs/` directory is needed in each host (or at the repo root).
- No database, API contract, or MCP tool surface changes.
