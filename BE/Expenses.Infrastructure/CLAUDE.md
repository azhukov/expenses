# Expenses.Infrastructure — adapters and composition root

References `Expenses.Application` only (never `Domain` directly — it arrives transitively). This is
where the technology lives: EF Core / Npgsql, PostgreSQL, image storage, the extraction engine, and
the DI wiring both hosts call.

## What belongs here

- `ExpensesDbContext`, entity configurations, migrations, and an
  `IDesignTimeDbContextFactory<ExpensesDbContext>` so `dotnet ef` needs no `--startup-project` (D15).
- Implementations of every port declared in [../Expenses.Application](../Expenses.Application):
  repositories, `IReceiptImageStore` over files under the receipt store (D11), the in-memory
  candidate store (D12), `PlaceholderReceiptExtractor` (D12) and later the real cascade (D20), the
  background extraction queue.
- **One public entry point for the hosts:** `AddExpensesInfrastructure(configuration)`. Adapters call
  that and nothing else from this project (D1). Keep every other type `internal` where you can.

## Mapping rules that are decisions, not preferences

- **Tables and columns are lower snake_case (D23, reversed).** `ToTable` names the table in
  snake_case; every property gets an explicit `HasColumnName` in a configuration, because a
  snake_case column never spells the same as its PascalCase property — there is no "only where it
  differs" case here, unlike a same-case convention. Foreign-key shadow properties
  (`Property<long?>("PurchaseId")`) and their auto-created indexes need the same explicit
  `HasColumnName` / `HasDatabaseName`, or EF names them from the C# property and leaves a mixed-case
  identifier behind. SQL written by hand — a check constraint, an index filter, a hand-edited
  migration — needs no quoting, because PostgreSQL folds an unquoted identifier to lower case
  anyway. Index and constraint names stay snake_case with `ix_` / `ck_` / `FK_` prefixes, as they
  always were.
- Money is `numeric(19,2)`, quantity `numeric(12,3)`, `OccurredAt` is `timestamp` **without** time
  zone and is never converted (D5, D10). Never `float`, never `money`.
- Entities have private constructors and private setters — configure backing fields
  (`UsePropertyAccessMode`) rather than loosening the domain to suit EF.
- Receipt bytes are never in the database at all: `purchases.receipt_storage_key` locates a file
  under the configured receipt store, and the columns are set together or not at all (D11).
- Seeding is split: `units` by `HasData`, `categories` by `UseAsyncSeeding` upserting on `code`,
  because categories are user-editable (D15, D8).
- Database *creation* (encoding, ICU locale, collation) is outside migrations — it is a provisioning
  script (D13). Extensions `unaccent` and `pg_trgm` are `HasPostgresExtension` in a migration; search
  is trigram + GIN, not `tsvector` (D14).
- The duplicate guard is a unique index with a query-first fast path, and the race is expected to
  surface as a unique-violation to catch (D4).
- Migrate on startup in development only; anywhere else applies a migration bundle (D15).

Anything exercised here is tested in
[../tests/Expenses.Integration.Tests](../tests/Expenses.Integration.Tests) against real PostgreSQL
via Testcontainers (D17) — an in-memory provider would pass where the real database fails.
