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

- **Tables and columns are PascalCase and match the entity and property (D23).** `ToTable` names the
  table; `HasColumnName` appears **only** where the column name differs from the property — the
  owned `Receipt` value flattening onto `Purchases` is the one place it does. Anything you write as
  SQL — a check constraint, an index filter, a hand-edited migration — must double-quote every
  identifier, because PostgreSQL folds an unquoted one to lower case and the constraint would be
  created against a column that does not exist. Index and constraint names stay snake_case with
  `ix_` / `ck_` prefixes.
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
