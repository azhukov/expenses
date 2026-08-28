# tests

Test-first is the working discipline (D21): the tests for a task are written and failing before its
implementation exists. Unimplemented production members throw `NotImplementedException` with their
intended behaviour already in the XML doc.

| Project | Scope | Needs |
| --- | --- | --- |
| `Expenses.Domain.Tests` | Entities, invariants, the arithmetic oracle | nothing |
| `Expenses.Application.Tests` | Use cases over fake ports, the arithmetic oracle, plus the architecture rules | nothing |
| `Expenses.Integration.Tests` | EF mappings, indexes, collation, trigram search | Docker |

xUnit, `[Fact]`/`[Theory]`, no mocking framework — hand-written fakes of the Application ports.

## Conventions

- One test class per production type, `public sealed class XxxTests`, with a class-level XML doc
  naming the spec scenarios it covers.
- Method names are sentences: `Quantity_beyond_three_decimal_places_is_rejected`.
- Domain rules are asserted on the exception type and `ParamName` — the domain has no error type
  and no codes (D22): `Assert.Throws<ArgumentOutOfRangeException>(...)` then
  `Assert.Equal("quantity", error.ParamName)`. Where only the message carries the offending values
  (an `InvalidOperationException` from a reconciliation failure), assert a fragment of it and keep
  the fragment short, because messages are prose and may be reworded.
- Application and adapter tests assert on the **code**: `ApplicationErrors.QuantityPrecision`.
  Codes are contract; that is where the contract now lives.
- Domain and Application tests never touch a database (D17). If a test needs one, it is an
  integration test.
- Integration tests use Testcontainers with the same provisioning as production, including encoding
  and ICU collation — one test asserts the database collation so a mis-provisioned environment fails
  loudly (D13).
- [Expenses.Application.Tests/Architecture/](Expenses.Application.Tests/Architecture/) inspects the
  `.csproj` files directly rather than assemblies, because an unused reference the compiler elides
  is still a broken ring (D1).
