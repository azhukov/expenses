# Expenses.Application — use cases and ports

References `Expenses.Domain` only. No EF Core, no Npgsql, no `HttpClient`, no ASP.NET types, no MCP
types. Nothing here knows whether it was called by [../Expenses.Api](../Expenses.Api) or
[../Expenses.Mcp](../Expenses.Mcp) — that is the whole point of the ring (D1).

## What belongs here

- **Use cases** — one class per operation, orchestrating entities and ports. One use case is one
  transaction; the aggregate is saved whole (D16).
- **Ports** — interfaces the outer ring implements: repositories, `IReceiptImageStore`,
  `IReceiptExtractor`, a clock, a unit of work. Defined here, in terms of domain types and
  primitives, and named for what the application needs rather than for the technology behind them.
- **DTOs** — plain records for use-case input and output, so entities do not leak to adapters.
- **The error model** — [Errors/](Errors/). `ApplicationError` is what both adapters map from, and
  `ApplicationErrors` holds **every** stable code, including the ones a domain rule raises: the
  domain throws framework exceptions and declares no codes of its own (D22), so the use case that
  called the entity is what names the code. Catch the entity's exception where you called it and
  translate it there — never let an `ArgumentException` reach an adapter.
- **Everything the domain is not allowed to hold** — validators, services, records, pure functions
  over entities. [Extraction/](Extraction/) is the standing example: the D20 arithmetic oracle and
  the stable value names live here because they are functions and data, not entities.

## What does not

- Rules that are the defining behaviour of an entity — those live in
  [../Expenses.Domain](../Expenses.Domain), not in a validator here. The reverse also holds: a type
  that is not an entity never goes there, however domain-shaped it feels (D22).
- SQL, EF queries, migrations, HTTP status codes, JSON shapes, MCP tool schemas.
- Any hand-rolled type over a single primitive (D7) — the rule holds in this ring too.

## Conventions

- Async all the way; every port method takes a `CancellationToken`.
- Failures are `ExpensesException` carrying an `ApplicationError` — codes are contract, both adapters
  map them, so adding a case means adding a `const` to `ApplicationErrors`.
- Not-found, duplicate-code, inactive-reference and already-attached checks belong here: they need a
  repository to answer, so the domain cannot make them.
- Tests in [../tests/Expenses.Application.Tests](../tests/Expenses.Application.Tests) use in-memory
  fakes of the ports — never a database. That project also hosts the architecture tests.
