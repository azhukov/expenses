# Expenses.Mcp — MCP adapter

The second front door, a separate process so each interface is independently reachable (D1). Same
reference rule as [../Expenses.Api](../Expenses.Api): `Application` for everything, `Infrastructure`
only for `AddExpensesInfrastructure(configuration)`, and never the other adapter.

## Rules

- **No rule lives here.** A tool handler validates nothing the use case already validates; it maps
  arguments in and a result out.
- Tools speak `code`, not display names — `GROCERIES` is unambiguous where "Groceries" is not (D8).
  Same for unit codes.
- An `ExpensesException` becomes a tool error carrying the `ApplicationError` code and fields, with
  the message as prose an assistant can relay. Do not invent MCP-specific error text.
- stdio transport is the intended local-assistant experience; keep host setup free of anything that
  assumes a web host.
- Anything the API can do, this can do, and vice versa — a capability that exists on only one side
  means a use case was written in the wrong ring.

`Worker.cs` is still the template's ticking `BackgroundService`; it goes when the MCP server host
replaces it.
