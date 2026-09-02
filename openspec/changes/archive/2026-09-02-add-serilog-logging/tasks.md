## 1. Dependencies (no behaviour — scaffolding)

- [x] 1.1 Add `Serilog.Extensions.Hosting`, `Serilog.Sinks.Console`, `Serilog.Sinks.File`, and
      `Serilog.Settings.Configuration` package references to `Expenses.Infrastructure.csproj`.
- [x] 1.2 Add `Serilog.AspNetCore` package reference to `Expenses.Api.csproj`.
- [x] 1.3 Add `Serilog.Extensions.Hosting` package reference to `Expenses.Mcp.csproj` (already
      transitive via `Expenses.Infrastructure`, but confirm the host can call `UseSerilog`).

## 2. Shared logging setup (Expenses.Infrastructure)

- [x] 2.0 Write failing tests for `AddExpensesLogging`, covering: console sink targets stdout when
      `useStandardError: false` and stderr when `useStandardError: true`; a log file is created
      under `logs/` on first write (spec: "Log file created on startup"); minimum level and category
      overrides from `Logging:LogLevel` configuration are honored (spec: "Default level applies",
      "Category override applies").
- [x] 2.1 Implement `ExpensesLoggingExtensions.AddExpensesLogging` in `Expenses.Infrastructure`,
      taking the builder, `IConfiguration`, and a `useStandardError` flag; configure Serilog via
      `ReadFrom.Configuration`, a console sink writing to the requested stream, and a file sink
      rolling daily under `logs/` with a 31-day retained-file-count limit (D28, D29, D30, D31).
- [x] 2.2 Confirm `logs/` is excluded from source control for both hosts.

## 3. Wire up Expenses.Api

- [x] 3.0 Write a failing integration test that starts the Api host and asserts a log file appears
      under its `logs/` directory containing the startup log line (spec: "Api host console output",
      "Log file created on startup").
- [x] 3.1 Call `AddExpensesLogging(builder.Configuration, useStandardError: false)` in
      `Expenses.Api/Program.cs`, replacing the default logging provider.
- [x] 3.2 Wrap `app.Run()` in the try/fatal-log/finally-flush pattern (D32) and log a startup
      message before `app.Run()`.

## 4. Wire up Expenses.Mcp

- [x] 4.0 Write a failing integration test for the `stdio` transport that runs the host with logging
      at `Trace` level and asserts every line written to stdout parses as a valid MCP protocol frame
      — none are log lines (spec: "Mcp stdio-transport console output stays off stdout"). Write a
      second failing test for the `http` transport asserting its console log output goes to stdout
      (spec: "Mcp http-transport console output").
- [x] 4.1 Replace `builder.Logging.AddConsole(...)` in the `stdio` branch of
      `Expenses.Mcp/Program.cs` with `AddExpensesLogging(builder.Configuration, useStandardError:
      true)`.
- [x] 4.2 Add `AddExpensesLogging(web.Configuration, useStandardError: false)` to the `http` branch.
- [x] 4.3 Wrap both branches' run calls (`await hosted.RunAsync()`, `await host.RunAsync()`) in the
      try/fatal-log/finally-flush pattern (D32) and log a startup message before each.

## 5. Retention and rotation

- [x] 5.0 Write a failing test asserting that when more than 31 daily log files already exist in
      `logs/` at startup, the oldest are removed so at most ~31 remain (spec: "Old log files are
      cleaned up").
- [x] 5.1 Confirm the file sink's `retainedFileCountLimit` (set in 2.1) satisfies this test; adjust
      configuration if the default rolling behaviour doesn't prune eagerly enough. (It already did —
      the test in 5.0 passed immediately against the 2.1 implementation.)

## 6. Validation (no behaviour — full-suite check)

- [x] 6.1 Run `dotnet build BE/Expenses.sln` and `dotnet test BE/Expenses.sln` and confirm everything
      passes, including the new tests above. (6 pre-existing failures in the FiscalPortal/
      FiscalJourney/FiscalSourceReporting family reproduce identically on unmodified `main` — they
      depend on the real tax-authority portal being reachable and are unrelated to this change.)
