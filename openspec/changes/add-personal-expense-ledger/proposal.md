## Why

There is no system here yet — the repository is empty apart from OpenSpec scaffolding. We want a personal expense ledger that can be fed two ways: by typing an expense in seconds, or by uploading a receipt photo and letting it become itemised expense lines. Both paths must land in the same place, so that a single ledger answers "where did the money go" without the user caring how each entry arrived.

The second motivation is that this ledger should be usable *conversationally*. Exposing the same use cases over MCP means an assistant can record and query expenses directly, while a browser UI serves review, correction, and reporting. Building both front doors over one Application layer from the start is far cheaper than retrofitting the second one later.

## What Changes

- **New backend** in `BE/` — .NET 10, Clean Architecture, four rings: `Domain`, `Application`, `Infrastructure`, and two driving adapters (`Api`, `Mcp`).
- **New frontend folder** `FE/` reserved for a React application. This change does not build the UI; it establishes the contract the UI will consume.
- **Purchase as the aggregate root** — a purchase is a container of one or more expenses. A manually typed entry is the degenerate case: one purchase, one expense, no image. There is no separate "manual expense" concept.
- **Manual purchase entry** with expense lines carrying description, quantity, unit, unit price, amount, and category.
- **Merchant on a purchase** — where the money was spent, held as a learned dictionary keyed by tax identification number where a receipt provides one, with an optional chain-to-branch relationship. A purchase always retains the merchant text exactly as printed, whether or not it resolved to a dictionary entry.
- **List price and discount on an expense line** — real receipts print what an item normally costs alongside what was actually paid. Both are captured so the ledger can answer "how much did I save", without either becoming a second source of truth for the amount.
- **Receipt upload** — a single image is attached to a purchase, stored, and passed to a receipt extractor that produces candidate expense lines.
- **Layered extraction with an arithmetic oracle** — extraction runs as an ordered cascade of stages, cheapest first, each recording what it contributed. Two stages are built for real in this change: opportunistic fiscal-QR decoding of the stored image, and an arithmetic validator that checks an extraction against itself. The paid vision stages remain behind the `IReceiptExtractor` port with a placeholder adapter; choosing and integrating a real vision engine is still deferred to a later change.
- **Extraction confidence is computed, not reported** — a fiscalised receipt carries redundant arithmetic (lines sum to the total, list price minus discount equals the paid amount, the total implies the printed VAT). Whether the numbers were read correctly is therefore decidable rather than estimated, and it is what moves an extraction between `Extracted` and `NeedsReview`. Self-reported confidence is retained only for values arithmetic cannot check — descriptions, category guesses, merchant names.
- **Idempotency guard on purchases** — adding a purchase whose `(OccurredAt, Amount)` pair already exists returns the existing purchase as a success rather than creating a duplicate or raising an error. This protects against double submits, HTTP retries, and an assistant calling the tool twice.
- **Dictionary tables** for categories and units, each with a stable `code` and a display `name`. No translation tables.
- **Multilingual content support** at the storage layer — UTF-8, ICU root collation, and accent/typo-tolerant search — so receipts in any language can be stored, sorted, and searched.
- **PostgreSQL via EF Core**, schema managed by EF Migrations, with database provisioning (encoding and collation) handled outside EF because EF cannot set them.
- **No authentication** — single-user, single-currency. Explicitly out of scope, and the idempotency key has no user column to reflect that.

## Capabilities

### New Capabilities

- `purchase-recording`: Recording a purchase and its expense lines, the container relationship between them, the merchant a purchase was made at, list price and discount on a line, the reconciliation invariant between a purchase amount and the sum of its expenses, and the `(OccurredAt, Amount)` idempotency guard.
- `receipt-ingestion`: Uploading a receipt image, attaching it to a purchase, storing the bytes, capturing fiscal receipt identity where a receipt carries it, and the staged extraction cascade that turns an image into candidate expense lines — including arithmetic validation of a result, the placeholder vision stage, and how unvalidated or failed extractions surface.
- `reference-data`: The category, unit and merchant dictionaries — stable codes, display names, seeding, activation, hierarchy, how a learned dictionary differs from a seeded one, and how verbatim text captured from a receipt is preserved alongside the normalised reference.
- `api-surface`: The two front doors. REST endpoints for the React client including multipart receipt upload, and the MCP tool surface for assistant-driven recording and querying, both delegating to the same Application use cases.

### Modified Capabilities

None — there are no existing specs in `openspec/specs/`.

## Impact

- **New code**: `BE/Expenses.sln` with `Expenses.Domain`, `Expenses.Application`, `Expenses.Infrastructure`, `Expenses.Api`, `Expenses.Mcp`, and test projects. `FE/` remains an empty placeholder.
- **New dependencies**: EF Core with `Npgsql.EntityFrameworkCore.PostgreSQL`, the .NET MCP server SDK, a QR decoding library with an image codec for the opportunistic decode stage, and PostgreSQL extensions `unaccent` and `pg_trgm`.
- **New infrastructure**: a PostgreSQL instance. Local development runs it in Docker with an init script that creates the database with the required encoding and collation, since EF Migrations cannot express those.
- **Deferred by design**: the paid vision extraction stages, authentication, multi-currency, multi-user, basket-level discounts that do not belong to any single line, and querying a tax authority's receipt verification service. Each of these has a known migration cost recorded in `design.md` rather than being silently assumed away.
- **Measured before specified**: server-side QR decoding was spiked against a real photographed thermal receipt before being scoped. It failed across roughly three hundred preprocessing combinations, and the cascade is shaped around that result rather than around the assumption that it would work.
- **No existing behaviour is affected** — this is the first change in the repository.
