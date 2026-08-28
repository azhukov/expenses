# BE — Expenses backend

.NET 10, C# latest, nullable enabled, `TreatWarningsAsErrors`. Five production projects in four
Clean Architecture rings plus two adapter hosts (D1 in
`openspec/changes/add-personal-expense-ledger/design.md`):

```
Expenses.Domain          no dependencies at all — not even NuGet
Expenses.Application     -> Domain                use cases + ports
Expenses.Infrastructure  -> Application           EF Core, storage, extraction, DI wiring
Expenses.Api             -> Application, Infrastructure (composition only)
Expenses.Mcp             -> Application, Infrastructure (composition only)
```

[ARCHITECTURE.md](ARCHITECTURE.md) draws this, the ports across each boundary, and the extraction
cascade.

The reference rules are asserted by
[ProjectReferenceRulesTests.cs](tests/Expenses.Application.Tests/Architecture/ProjectReferenceRulesTests.cs).
Adding a `ProjectReference` or `PackageReference` that breaks a ring fails the build, not review.

## Working rules

- **The design document is the source of truth.** Decisions are numbered `D1`–`D21`; code comments
  cite them (`(D19)`). When you change behaviour a decision covers, update the decision in the same
  change — do not leave the two disagreeing.
- **Test-first (D21).** A task's tests are written before its implementation. Unimplemented members
  are `throw new NotImplementedException()` with their XML doc already stating the intended
  behaviour; fill them in, don't redesign them.
- **No rule lives in an adapter (D1).** If `Api` and `Mcp` would both need the same check, it belongs
  in `Application` or `Domain`.
- **`Expenses.Domain` holds entities and nothing else (D22).** Not a validator, not an exception
  type, not an error-code table, not a record. Enums that describe an entity's state are nested
  inside it. Anything else that would go there goes in `Application` instead.
- **Tables and columns are PascalCase (D23).** They are named exactly as the entity and property they
  map — `Purchases.OccurredAt`, `Merchants.TaxId` — so a column is named explicitly in a
  configuration only where the mapping genuinely differs from the property. PostgreSQL folds an
  unquoted identifier to lower case, so **every hand-written identifier must be double-quoted**: in a
  check constraint, an index filter, raw SQL in a test, or a `psql` session —
  `select "OccurredAt" from "Purchases"`. Index and constraint names are the exception and stay
  snake_case behind `ix_` / `ck_`.
- **Do not introduce a type over a single primitive (D7).** A `decimal` amount stays a `decimal`; a
  quantity stays a `decimal`. In the outer rings, shared rules over a primitive are static
  functions, not wrappers.
- Comments explain *why*, in prose, at the density of the surrounding file. Don't narrate the code.

## Commands

```
dotnet build BE/Expenses.sln
dotnet test  BE/Expenses.sln
dotnet test  BE/tests/Expenses.Domain.Tests        # no database needed
```

Integration tests need Docker (Testcontainers, real PostgreSQL — D17).
