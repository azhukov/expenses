# Expenses.Api — HTTP adapter

One of two front doors. References `Expenses.Application` (everything it works with) and
`Expenses.Infrastructure` (solely to call `AddExpensesInfrastructure(configuration)` at startup).
It must never reference [../Expenses.Mcp](../Expenses.Mcp), and a test enforces that (D1).

## Rules

- **No rule lives here.** An endpoint parses the request, calls one use case, and shapes the
  response. If you find yourself writing a check that [../Expenses.Mcp](../Expenses.Mcp) would also
  need, it belongs in `Application` or `Domain`.
- Minimal APIs, grouped by resource in endpoint files — `Program.cs` stays composition plus
  `MapXxxEndpoints()` calls.
- Domain entities never appear in a request or response body; use the Application DTOs.
- `ExpensesException` is translated in one place (an exception handler / `IProblemDetailsService`)
  into `ProblemDetails` that carries the `ApplicationError` code and fields verbatim — the code is
  the contract, so don't re-map it per endpoint.
- Startup is the only place that touches `Infrastructure` types.

`Program.cs` still holds the template's `WeatherForecast` sample; it goes when the first real
endpoint lands.
