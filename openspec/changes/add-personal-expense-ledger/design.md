## Context

Greenfield repository — see `proposal.md` for motivation. The only pre-existing constraints are the ones chosen during exploration and recorded as decisions below: .NET 10, Clean Architecture, PostgreSQL with EF Core and EF Migrations, React in `FE/`, single user, single currency, and receipt extraction present as a seam but mocked.

Two consequences shape almost everything here. First, there are two driving adapters (HTTP and MCP) over one Application layer, so no business rule may live in either adapter. Second, the ledger must accept text in any language, which is a storage and collation concern rather than a translation concern — there are no translated strings anywhere in this design.

## Goals / Non-Goals

**Goals:**

- One Application layer, two interchangeable front doors, with the boundary enforced by project references rather than convention.
- A data model where a manually typed expense and a receipt-derived expense are the same shape, so queries and reports never branch on provenance.
- Duplicate submission is absorbed silently and safely, including under concurrency, without a database round trip on the common path becoming a correctness dependency.
- Every seam that a real extraction engine will later need — port, lifecycle, candidate storage, staged cascade, validation, re-extraction — exists and is exercised now, so introducing a real engine is a configuration of stages rather than a reshaping of the pipeline.
- Schema fully described by EF Migrations, with the single exception that EF provably cannot express, called out and handled explicitly.
- Every behaviour arrives with a test that was seen to fail first, derived from the spec scenario it implements.

**Non-Goals:**

- Any React implementation. `FE/` stays empty; this change defines the contract it will consume.
- Real image analysis of any kind. The cascade's paid stages are placeholders; only its free stages — QR decoding and arithmetic validation — do real work here.
- Any abstraction over PostgreSQL. EF Core is the persistence abstraction; PostgreSQL-specific features are used deliberately and directly.
- Generic multi-tenancy or a plugin architecture. Both are speculative here.

## Decisions

### D1 — Four rings, five projects, two adapters

```
BE/
  Expenses.Domain          no dependencies at all
  Expenses.Application     -> Domain            use cases + ports
  Expenses.Infrastructure  -> Application       EF Core, storage, extraction, DI wiring
  Expenses.Api             -> Application, Infrastructure (composition only)
  Expenses.Mcp             -> Application, Infrastructure (composition only)
  tests/
    Expenses.Domain.Tests
    Expenses.Application.Tests
    Expenses.Integration.Tests
```

`Api` and `Mcp` never reference each other. They reference `Infrastructure` solely to call a single `AddExpensesInfrastructure(configuration)` extension method at startup; every other type they touch comes from `Application`.

**Why not a separate composition project:** a sixth project whose only content is one extension method adds a reference edge without removing one. The composition root extension living in `Infrastructure` is the conventional .NET placement and keeps the adapter projects thin.

**Why two host processes rather than one host exposing both:** `api-surface` requires each interface to be independently reachable. Two processes make that structural rather than a promise. `Expenses.Mcp` supports stdio transport, which is the natural local-assistant experience and is impossible to offer from a shared web host. Cost: configuration and migration-on-startup logic exist twice, which is why both are reduced to one call into `Infrastructure`.

### D2 — Purchase is the aggregate root; Expense is inside it

`Purchase` is the only aggregate root. `Expense` has no repository, no independent lifecycle, and is never loaded on its own. The reconciliation invariant between purchase amount and the sum of expense amounts is enforced inside the aggregate, not in a validator or a database constraint, because it is the defining rule of the type.

A manual entry constructs a purchase with a single expense. There is no separate manual path.

**Alternative considered — Expense as its own root with an optional purchase reference:** rejected because it makes the reconciliation invariant unenforceable (nothing owns it), and because every report would then have to handle expenses that belong to a purchase and expenses that do not.

### D3 — The duplicate guard is an idempotency guard, not deduplication

Matching on `(OccurredAt, Amount)` and returning the existing purchase is a guard against the *same request* arriving more than once — a double-clicked button, an HTTP retry, an assistant calling a tool twice. It is deliberately not an attempt to recognise the same real-world purchase captured two different ways.

This distinction is recorded because the naive expectation is the opposite, and the behaviour looks broken if you expect deduplication:

- A purchase typed as a date only becomes `00:00:00`; the same purchase later uploaded as a receipt carries a real time. They will not match. That is correct for an idempotency guard and wrong for deduplication.
- Two genuinely different same-day purchases of the same amount with no times *will* collide. `purchase-recording` specifies the escape hatches: supply a time, or record them as two expenses within one purchase.

Naming follows the intent throughout — `DuplicatePurchaseGuard`, `PurchaseAlreadyRecorded` — so nobody later reads it as semantic deduplication.

**Returning success rather than a conflict is load-bearing for MCP.** An assistant that receives an error will try to route around it: rephrase, retry with a nudged amount, apologise to the user. An assistant that receives "already recorded, here it is" simply proceeds. The HTTP adapter distinguishes the two outcomes by status code (201 versus 200), the MCP adapter by wording in the successful result.

### D4 — The guard is enforced by a unique index, with a query-first fast path

```
UNIQUE INDEX ix_purchases_occurred_at_amount ON purchases (occurred_at, amount)
```

Flow: query for a match first and return it if found; otherwise insert; if the insert violates the unique index, re-query and return the winner.

**Why both halves:** the fast path keeps the common case to one round trip and produces a clean result without relying on exceptions for control flow. The index is what makes the guarantee true — the precise case being guarded against is two identical requests arriving together, which a check-then-insert loses. Relying on the application check alone would leave the guard broken exactly when it matters most.

**Alternative considered — a client-supplied UUIDv7 primary key as the idempotency token:** cleaner, since the primary key itself enforces it. Rejected because the identifier was specified as an artificial, database-owned surrogate. Recorded here as the natural upgrade path if the guard ever needs to be strengthened.

### D5 — `occurred_at` is `timestamp without time zone`

A receipt prints local wall-clock time and carries no offset. Storing it as an absolute instant would require inventing a timezone in order to normalise it, which is fabricated precision. Three concrete consequences drove the choice:

- Midnight stays midnight. With `timestamptz`, a date-only purchase normalised from a non-UTC zone shifts to the previous day in storage, which both moves it in monthly reports and destabilises the idempotency key.
- The idempotency key is stable regardless of where the client is.
- Npgsql enforces `DateTimeKind.Utc` on `timestamptz` writes and throws otherwise, which would mean converting a value that has no timezone to and from UTC on every read and write.

**Trade-off accepted:** a server-hosted instance used from another timezone has an ambiguous notion of "today". For a single-user personal ledger this is a fair trade. `DateTime` with `Kind.Unspecified` is used throughout; `DateTimeOffset` is deliberately not used, since an offset here would be invented.

### D6 — `Amount` means money; `Quantity` means quantity

`Purchase.Amount` and `Expense.Amount` are monetary. `Expense.Quantity` is how much of the thing was bought. No word carries two meanings across the model.

Within an expense, `Amount` is authoritative and `Quantity`, `Unit` and `UnitPrice` are descriptive. They are permitted to disagree, and the system must not recompute or "correct" the amount from them, because real receipts do not reconcile: promotions, three-for-two offers, weight rounding, and per-unit prices rounded for display all produce lines where quantity times unit price is not the line total. Only `Amount` participates in reconciliation with the purchase.

`Quantity` is non-nullable and defaults to `1`, so the simple case reads coherently without a nullable that every consumer must guard.

`ListUnitPrice` and `DiscountAmount` join `Quantity`, `Unit` and `UnitPrice` on the descriptive side — see D19. Nothing on the descriptive side participates in reconciliation, and nothing on it is recomputed from anything else.

### D7 — Money is a value object over `decimal`, with no currency field

`Money` is a `readonly record struct` wrapping a single `decimal`, giving the non-negativity rule and precision one home rather than scattering guard clauses. It is mapped as an EF complex type onto a single `numeric(19,4)` column, so the storage shape is identical to a bare decimal.

**Why not a bare `decimal`:** the validation would have to be duplicated at every entry point, and adding a currency later would be a model-wide edit instead of a contained one.

**Why no currency field now:** single-currency is a specified constraint. A currency column that is always the same value is noise that every query and index must carry. Adding it later is one migration plus one index rebuild, which is a known and acceptable cost.

### D8 — Reference data: dictionary tables keyed by immutable `code`

`categories` and `units` are ordinary tables. `code` is the stable identity used by seed data, tests, MCP tool arguments, and any future import. `name` is display-only and freely editable.

**Why `code` matters more than usual here:** it does triple duty — the natural key for idempotent seeding, the argument vocabulary for MCP tools (an assistant passing `GROCERIES` is unambiguous in a way that passing "Groceries" is not), and the stable reference that survives a rename.

**No translation tables.** Multilingual support means content is stored in whatever language it arrived in — not that concepts have several renderings. Category and unit names are single strings.

Categories are hierarchical via a nullable `parent_id`, with cycle rejection in the domain. This was cheap to include now and expensive to retrofit, since every report and rollup would need reworking.

Retirement is by an `is_active` flag rather than deletion, because deleting a category would either orphan or rewrite history. `is_system` marks seeded entries as undeletable.

### D9 — Verbatim receipt text is stored beside every normalised reference

```
expenses.category_id   FK -> categories   nullable
expenses.category_raw  text               nullable
expenses.unit_id       FK -> units        nullable
expenses.unit_raw      text               nullable
purchases.merchant_id  FK -> merchants    nullable
purchases.merchant_raw text               nullable
```

Extraction fills the `_raw` columns unconditionally and the foreign keys only when a confident match exists. This is what makes the closed unit dictionary safe: real receipts print `Bund`, `Stk`, `Pack`, `100g`, `Pfand`, and a closed enum alone would discard that text at the moment of capture. Keeping the raw string means matching can be improved later and re-run over historical rows without re-reading any images.

The columns are populated by extraction but are not exclusive to it; manual entry may set them too.

`merchant_raw` follows the same rule one level up, on the purchase rather than the expense — see D18. Deliberately the same mechanism rather than a new one: there is one answer in this design to "what happens when receipt text does not match anything we know", and it is to keep the text.

### D10 — PostgreSQL column types

| Column | Type | Rationale |
| --- | --- | --- |
| all `id` | `bigint GENERATED ALWAYS AS IDENTITY` | Database-owned artificial key, as specified |
| `purchases.occurred_at` | `timestamp` | See D5 |
| `purchases.amount`, `expenses.amount`, `expenses.unit_price` | `numeric(19,4)` | Exact decimal; never `float` or `money` |
| `expenses.quantity` | `numeric(12,3)` | Fractional mass and volume; three decimals covers fuel volumes |
| `expenses.list_unit_price`, `expenses.discount_amount` | `numeric(19,4)` nullable | Descriptive, per D19; null means the receipt printed no discount, not a discount of zero |
| `merchants.tax_id` | `varchar(32)` nullable, `UNIQUE` | Natural key where a receipt carries one, per D18 |
| `merchants.parent_id` | `bigint` nullable, FK -> `merchants` | Branch to chain |
| `purchases.merchant_id` | `bigint` nullable, FK -> `merchants` | Nullable: a purchase need not name where it happened |
| `receipt_images.fiscal_*` | `text` nullable | Fiscal receipt identity as printed, per D20; no format is imposed |
| descriptions, names, `_raw` columns | `text` + `CHECK (length(...) <= n)` | In PostgreSQL `text` and `varchar(n)` perform identically; a length `CHECK` can be altered cheaply while `varchar(n)` widening rewrites intent into the type |
| `categories.code`, `units.code` | `varchar(64)` / `varchar(16)`, `UNIQUE` | ASCII, uppercase, stable |
| `receipt_images.content` | `bytea` | See D11 |
| `receipt_images.content_hash` | `bytea(32)`, `UNIQUE` | SHA-256 of the raw bytes |

### D11 — Receipt bytes live in `bytea`

Images are stored in the database rather than on disk or in object storage.

**Why:** it keeps image and metadata in one transaction and one backup, and removes an entire class of bug where the row and the file disagree — an orphaned file, or a row pointing at a file that was never written. For a personal ledger the volume is small: a few thousand receipts at roughly 1 MB is single-digit gigabytes.

**Cost accepted:** larger dumps, and image bytes travelling through the connection rather than being served directly. Mitigated by never selecting `content` unless the bytes are actually being served — enforced by keeping `ReceiptImage` content behind a separate query rather than a navigation property that could be loaded accidentally.

Access goes through an `IReceiptImageStore` port, so moving to object storage later is one adapter plus a data migration and touches nothing above `Infrastructure`.

### D12 — Extraction: port now, placeholder adapter now, real engine later

D20 refines this decision into an ordered cascade and replaces model-reported confidence with computed arithmetic validation. Read the two together; where they differ, D20 wins.

```
Application:     IReceiptExtractor  ->  ExtractionResult { lines[], confidences, engine, version }
Infrastructure:  PlaceholderReceiptExtractor   (this change)
                 VisionReceiptExtractor        (later change)
```

Everything except the analysis is real: the lifecycle states, candidate storage, validation, `NeedsReview` and `Failed` handling, re-extraction, and the engine identifier recorded on every result. (D20 narrows what drives `NeedsReview` from a reported confidence threshold to an arithmetic check.) The placeholder derives deterministic output from the image hash and can be configured to simulate low confidence and failure, so both non-happy paths are exercised.

**Why candidates are stored separately from expenses** rather than written straight into the purchase: extraction is a suggestion, and the reconciliation invariant would otherwise be violated by an engine that misreads a total. Storing candidates in their own table lets an unconfirmed extraction exist without the aggregate ever being invalid, and makes re-extraction non-destructive to confirmed data.

Recording the engine name and version on each result means a later real engine can be told apart from placeholder output already in the database — which matters, because rows created now will still be there.

Extraction runs off the request path via an in-process background queue (a bounded `Channel` drained by a `BackgroundService`). **Trade-off:** work queued but not yet run is lost on restart. Mitigated by `Pending` being persisted, so a startup sweep re-queues anything still pending. A durable queue is unnecessary for a single-user ledger and is the obvious upgrade if that changes.

### D13 — Database provisioning is outside EF Migrations

EF connects to a database that already exists; it cannot set the encoding, locale provider or collation the database was created with, and none of those can be altered afterwards without a dump and restore.

```sql
CREATE DATABASE expenses
  ENCODING 'UTF8'
  LOCALE_PROVIDER icu
  ICU_LOCALE 'und'
  TEMPLATE template0;
```

This lives in a Docker `docker-entrypoint-initdb.d` script for local development and must be part of whatever provisions any other environment. ICU root collation (`und`) sorts mixed-language content sensibly rather than by raw byte order. Per-column collations were rejected because no column here is reliably in one language.

**Risk of getting this wrong is that nothing appears broken** — sorting is just quietly incorrect, and the fix is a migration of the whole database. An integration test asserts the database collation at startup so a mis-provisioned environment fails loudly and immediately.

Everything else is EF Migrations, including `HasPostgresExtension` for `unaccent` and `pg_trgm`.

### D14 — Search uses `unaccent` + `pg_trgm`, not full-text search

Trigram matching over expense descriptions and over merchant names — both the dictionary `merchants.name` and the verbatim `purchases.merchant_raw`, since an unmatched merchant is exactly the one a user will search for by half-remembered name — with a GIN index using `gin_trgm_ops`.

**Why not `tsvector`:** full-text search needs a per-language configuration, which means correctly detecting the language of every row. Receipt line items are short, abbreviated and frequently mis-OCR'd — poor input for stemming, and the language detection would itself be unreliable. Trigram matching is language-agnostic and tolerates exactly the character-level noise this data has.

### D15 — Migrations: design-time factory, split seeding strategy, bundles for deployment

**Design-time factory.** Migrations live in `Infrastructure`, which is not a startup project, and there are two hosts. An `IDesignTimeDbContextFactory<ExpensesDbContext>` in `Infrastructure` reads its own connection string so `dotnet ef` commands need no `--startup-project` and no host is privileged over the other.

**Seeding is split by mutability:**

| Data | Mechanism | Why |
| --- | --- | --- |
| `units` | `HasData` | Fixed reference data. Model-managed rows are correct here — nobody edits them. |
| `categories` | `UseAsyncSeeding`, upsert by `code` | Seeded *and* user-editable. `HasData` treats the model as the source of truth: removing an entry emits a `DELETE`, and a user's rename would be reverted or would conflict on the next migration. |

This is the second reason `code` is load-bearing (D8) — it is the idempotent seeding key.

**Applying migrations.** Local development migrates on startup for convenience. Anywhere else uses a migration bundle as a deployment step: two hosts migrating on startup would race, and neither host should hold DDL rights at runtime.

### D16 — One transaction per use case; the aggregate is saved whole

A purchase and its expenses are written in a single `SaveChanges`. Confirming extraction candidates replaces expenses and clears candidates in one transaction. There is no scenario in this change where a partially written purchase is acceptable, so no compensating logic exists — the transaction boundary is the aggregate boundary.

### D17 — Integration tests run against real PostgreSQL

Testcontainers, using the same provisioning as production including encoding and collation. The behaviours that matter most here are ones an in-memory provider cannot express: unique index violation under concurrency (D4), `numeric` precision, ICU collation ordering, and trigram search. An in-memory provider would make those tests pass while the real database failed.

Domain and Application tests need no database.

### D18 — Merchant is a learned dictionary, keyed by tax identification number

```
merchants.id         bigint identity
merchants.name       text                      -- "AROMA"
merchants.tax_id     varchar(32) UNIQUE null   -- "02440261"
merchants.parent_id  bigint null -> merchants  -- branch -> chain
merchants.is_active  boolean

purchases.merchant_id   FK -> merchants  nullable
purchases.merchant_raw  text             nullable
```

A fiscalised receipt names its merchant at up to six levels of precision. A real example:

```
AROMA                  brand / chain          -> what a report should group by
DOMACA TRGOVINA doo    legal entity
PIB 02440261           tax identification     -> the stable natural key
Aroma 034              branch
Filipa Kovacevica 24   branch address
ENU 20345/hi8211c718   point-of-sale terminal
```

Only two of those are modelled: a merchant, and optionally its parent merchant. The chain is the parent, the branch is the child, and a purchase points at whichever one was identified. The rest — legal entity, address, terminal — is kept in `merchant_raw` and not decomposed, because nothing in this change reads it and a wrong decomposition is worse than an undecomposed string.

**Why merchants are unlike categories and units, and why that is stated rather than assumed.** D8 makes `code` mandatory, stable and known in advance, because categories and units are *seeded*. Merchants are *discovered* — the first time a receipt from a new shop is ingested, an entry appears. Three consequences follow, and each is a deliberate divergence from D8:

- **There is no `code`.** A merchant's stable identity is its `tax_id` where the receipt prints one, and nothing where it does not. `tax_id` is therefore nullable and unique-when-present, not a mandatory key.
- **There is no seeding and no `is_system`.** No merchant ships with the product, so nothing needs protecting from deletion by a seed run.
- **A merchant may be unidentifiable.** A market stall issues no tax number and possibly no name. `purchases.merchant_id` is nullable and `merchant_raw` carries whatever was printed.

**Why `tax_id` rather than name as the natural key:** the name on the paper is a brand, is inconsistently abbreviated, and is the field OCR is most likely to mangle. The tax number is fixed-format, machine-checkable, and printed on every fiscalised receipt in the jurisdictions this ledger is used in. Matching on it means "the same shop" is a fact rather than a fuzzy string comparison.

**Alternative considered — merchant as free text on the purchase, with no dictionary:** simpler, and defensible for a single-user ledger. Rejected because "what do I spend at this shop" is one of the two questions the ledger exists to answer (the other being "on what"), and answering it over free text means grouping by a string that varies per receipt. The `merchant_raw` column means nothing is lost by also having the dictionary.

**Alternative considered — modelling terminal, address and legal entity as their own columns:** rejected as speculative. They are on the paper, they are in the stored image, they are in `merchant_raw`, and no requirement reads them. Adding them later is one migration.

### D19 — Discount is descriptive; the percentage is derived and never stored

A discounted line prints three numbers and one of them is redundant:

```
Sladoled Milka Mini Sticks MPK 6x50ml   1 x 4.49   4.49
    Cijena: 8.50   Popust: 47.18% (4.01 Eur)
             ^^^^           ^^^^^^  ^^^^
             list           derived paid-off
```

Stored:

```
expenses.amount            4.49   authoritative, reconciles with the purchase
expenses.list_unit_price   8.50   descriptive
expenses.discount_amount   4.01   descriptive, a positive magnitude
```

**The percentage is not stored.** `47.18%` is a rounded rendering of `4.01 / 8.50`; keeping it creates a second, lossy source of truth for one fact and guarantees a row will eventually exist where the stored percentage and the stored amounts disagree. It is computed for display.

**`discount_amount` is a positive magnitude, not a negative amount.** This is what keeps D7's non-negativity rule intact. The discount is a number subtracted from a list price, not money with a sign. `Money` never becomes signed and no guard clause is relaxed.

**Reconciliation is untouched.** Only `Amount` participates, exactly as in D6. A purchase of `8.48` containing lines of `4.49` and `3.99` reconciles whether or not either line carries a discount, and the discount columns are never consulted when checking it.

**"How much did I save" is derived, not stored.** A purchase-level saved total is `SUM(discount_amount)` over its expenses. Storing a rollup would be a third source of truth for the same fact and would need maintaining on every edit.

**Null means "no discount printed", not "a discount of zero".** Both columns are nullable and are set together or not at all. This matters for reporting: a ledger that cannot distinguish "not discounted" from "discounted by nothing" reports a misleading saving rate.

**What this deliberately does not cover:** a discount applied to the *basket* rather than to a line — a loyalty coupon taken off the total. It cannot be expressed here, and forcing it into a line would break reconciliation. It is recorded as an open question rather than solved, because solving it is the same decision as whether `Money` may ever be negative, and that decision should be made against a real receipt that needs it.

### D20 — Extraction is an ordered cascade, and its confidence is computed rather than reported

This refines D12. The port, the lifecycle, the candidate table and the engine-identity recording all stand. What changes is that extraction is not one engine but an ordered sequence of stages, and that the threshold separating `Extracted` from `NeedsReview` is an arithmetic check rather than a number a model reports about itself.

```
  stage 0  fiscal QR decode (client, at capture)   free      out of scope here (FE)
  stage 1  fiscal QR decode (server, on the file)  free      built in this change
  stage 2  vision extraction - cheap tier          paid      placeholder in this change
  stage 3  arithmetic validation                   free      built in this change
  stage 4  vision extraction - expensive tier      paid      placeholder in this change
                                                             fires only when stage 3 fails
```

Stages 1 and 3 are real. Stages 2 and 4 are two configured instances of the same placeholder in this change, and two configured instances of a real engine later; the cascade does not change when they become real, which is the point of building it now.

**Why the cascade is cheapest-first with an expensive fallback rather than one good engine:** stage 3 makes it safe. Without a way to tell whether a cheap result was correct, a cascade is just a way of getting worse answers more cheaply. With one, the expensive stage runs only on the receipts that actually needed it.

**Why arithmetic validation replaces reported confidence.** A fiscalised receipt is redundantly encoded. Taking one real receipt as the example:

```
  4.49 + 3.99       == 8.48   lines sum to the total
  8.50 - 4.01       == 4.49   list minus discount equals paid   (per line)
  7.50 - 3.51       == 3.99
  8.48 / 1.21 * .21 == 1.47   total implies the printed VAT
  IKOF from stage 1 == IKOF read by stage 2                     (when stage 1 hit)
```

Four independent checks over the same numbers. A misread digit anywhere in the numeric fields breaks at least one of them. This is a proof, not an estimate, and it is strictly better than a model reporting `0.87` about its own work.

**Consequence — confidence is split by field kind, not carried as one number:**

| Field kind | How correctness is established |
| --- | --- |
| Amounts, quantities, discounts, tax, total | Arithmetic validation. Decidable. Drives `Extracted` versus `NeedsReview`. |
| Fiscal identifiers | Cross-check against a stage 1 QR decode when there was one; otherwise unverified. |
| Descriptions, merchant name, category and unit guesses | Model-reported confidence. Not decidable. Marked for review but never blocks. |

`NeedsReview` therefore means "the numbers do not add up" or "an unverifiable field was read with low confidence", and the reason is recorded per field rather than as a single score. This **closes the open question** carried in D12 about choosing a confidence threshold against a placeholder — for every field that matters, there is no threshold to choose.

**Why server-side QR decoding is a stage and not the primary path.** It was measured before it was specified. A .NET spike ran ZXing over a real photographed thermal receipt across roughly three hundred combinations — full resolution, a downscale ladder, three crop boxes, global and adaptive Otsu thresholding, morphological correction for thermal ink bleed, hybrid and pure binarizers:

```
  full res                        1652 ms   miss
  downscale 0.50 / 0.35 / 0.25   150-294    miss
  bottom crop                      664 ms   miss
  tight crop, five scales           4-165   miss
  + Otsu, adaptive Otsu, dilation radius 1-3   miss
  ------------------------------------------------
  ~300 combinations                          0 hits
```

The code is not at fault and the crop is not at fault: all three finder patterns are intact. The symbol is simply dense — roughly 69 to 73 modules, version 13 or 14 — printed on thermal paper where black modules bleed together, photographed at about nine pixels per module with slight blur. This is a signal-quality wall, not a tuning problem.

**So stage 1 is opportunistic, and the system must be correct when it misses.** It is worth building anyway: it costs nothing, it takes single-digit to low-hundreds of milliseconds, and when it hits it yields an exactly-correct fiscal identity that cross-checks stage 2. It must never be a prerequisite for anything.

**The stage that actually wants the QR is stage 0, in the browser, and it is out of scope here.** A live camera stream can autofocus, retry, and tell the user to hold steady — which is precisely what a single uploaded still cannot do. When `FE/` is built, QR decoding belongs at capture, and the fiscal identity it yields can be checked against the ledger *before* four megabytes are uploaded. The backend contract is shaped for that now: fiscal identifiers are accepted alongside an upload rather than only discovered from it.

**Why decoding the QR is worth less than it appears.** Every field the fiscal QR encodes is also printed as plain text on the receipt — the tax number, the receipt ordinal, the timestamp, the total, the terminal codes, and both fiscal identifiers. A QR decode is a convenience, not a unique source. The only thing it would uniquely unlock is calling the tax authority's verification service for authoritative data, and that is deliberately not attempted — see the open questions.

**Every stage records what it contributed.** D12 already requires an engine name and version on each result; the cascade extends that to which stage produced each field, so a value read by the cheap tier, a value corrected by the expensive tier, and a value decoded exactly from a QR are distinguishable in the stored data forever.

### D21 — Test-first is the working discipline, and the task list enforces it

Every task that changes behaviour is preceded by the task that writes its failing test. This is recorded as a decision rather than left as a preference, because it is the kind of rule that erodes silently under deadline and is expensive to reintroduce once a codebase has grown without it.

**Why here rather than in a spec.** A `spec.md` requirement states what the ledger does; "SHALL be developed test-first" is not something the running system can satisfy or violate. The discipline binds in three places instead — `openspec/config.yaml` under `operations.apply.guidance` so it governs the apply phase, `rules.tasks` so future task lists are written this way, and the task list itself, where each behaviour section starts at `X.0` with its tests.

**Red before green is the load-bearing half.** A test written after the implementation passes on the first run, and a test that has never failed has not been shown to test anything. Several of the rules in this change would pass a vacuous test happily — a reconciliation check that never runs, a validator that returns "not applicable" for everything, a decoder-miss path that is never taken. Requiring the failing run is what distinguishes those from working code.

**Spec scenarios are the test cases.** The four spec files carry 156 WHEN/THEN scenarios. They are already in the shape of test names and assertions, and they were written before any code exists, which is the same ordering property test-first is after. Tests derive from scenarios rather than from the implementation the author has in mind; the mapping is one test per scenario wherever a scenario is directly executable.

**Where the discipline is inverted, and why that is stated.** D17 puts the tests that matter most — unique-index behaviour under concurrency, `numeric` precision, ICU collation, trigram search — against real PostgreSQL via Testcontainers, which is slow. The inner red-green loop is therefore the domain and application tests, which need no database. Integration tests are still written before the feature they cover, but they are expected to stay red for longer, and that is not a licence to defer writing them until the feature is done.

**What is exempt, named rather than left ambiguous:** solution and project scaffolding, package references, the `docker-compose.yml` and provisioning script, EF-generated migration files, OpenAPI generation, and documentation. These produce no behaviour of their own. Everything they enable is covered by tests elsewhere — the provisioning script, for instance, is exempt while the startup assertion that the database was provisioned correctly is not.

**Cost accepted.** Test-first is slower per task and faster per change, and the arithmetic on that is well established enough not to relitigate here. The specific local cost is that the cascade, the validator and the merchant matcher all need their seams to exist before their first test can run, which front-loads the port definitions in section 3. That is the design signal working as intended rather than an obstacle.

## Risks / Trade-offs

- **The idempotency guard blocks legitimate identical same-day purchases** (two €2.90 coffees, both without times) → Specified explicitly in `purchase-recording` with two escape hatches, and the second-entry response must name them rather than reporting a bare conflict. Accepted deliberately: the guard mildly pressures the user toward more precise data, which is a defensible bias for a ledger.

- **`timestamp without time zone` is ambiguous for a travelling user of a hosted instance** (D5) → Accepted for a single-user ledger. Recorded here so it is a known trade rather than a surprise. Migrating later means choosing a timezone to interpret existing values in, which is a data decision, not just a schema one.

- **Mocked extraction can create false confidence that the feature works** → Every result records its engine, the specs require placeholder output to be identifiable wherever candidates are surfaced, and the placeholder must be able to produce `NeedsReview` and `Failed` so those paths are genuinely exercised rather than theoretically present.

- **`bytea` growth degrades dumps and restores as the ledger ages** (D11) → Bounded by the single-user scale; `IReceiptImageStore` keeps the migration path to object storage contained to one adapter.

- **Database provisioning drift between environments fails silently** (D13) → A startup integration check asserts encoding and collation, converting a silent wrong-sort into a loud failure.

- **Two adapters drift apart, one growing a rule the other lacks** → `api-surface` requires identical behaviour, and integration tests exercise the same operations through both. The project reference graph prevents an adapter from reaching persistence directly, but nothing structurally prevents a validation rule being added in an adapter; this is a review responsibility.

- **Queued extraction work is lost on restart** (D12) → `Pending` is persisted and re-queued by a startup sweep, so nothing is permanently stranded.

- **Server-side QR decoding will usually miss, and building it could create false confidence that fiscal identity is always available** (D20) — measured at zero hits over ~300 preprocessing combinations on a real receipt photo. Mitigated by making stage 1 opportunistic by construction: no stage depends on it, and the specs require the pipeline to behave identically when it misses. The honest expectation recorded here is that it hits rarely on photographed thermal receipts and often on flat, well-lit scans.

- **Arithmetic validation cannot check the fields users actually read** (D20) — a description misread as "Sladoled MILKA butter" instead of "Sladoled MILKA butter MPK 3x90ml" passes every arithmetic check. The oracle proves the numbers, not the words. Mitigated by keeping model-reported confidence for exactly those fields and marking them for review, and by the design already requiring candidates to be confirmed by a human beside the stored image.

- **The cascade makes cost non-obvious per receipt** — a receipt that fails stage 3 costs several times one that passes. At single-user volume the absolute numbers are negligible, but the stage that fired must be recorded per extraction so the shape of that distribution is visible rather than inferred.

- **A merchant dictionary learned from OCR will accumulate near-duplicate entries** (D18) — the same shop read as "AROMA" and "AR0MA" becomes two merchants when neither receipt printed a usable tax number. Mitigated by `tax_id` matching where present, by trigram search over merchant names (D14), and by `merchant_raw` making a later re-match possible without re-reading images. Not mitigated at all where no tax number exists; accepted.

- **Test-first erodes quietly, and nothing in the repository would notice** (D21) → The rule lives in `openspec/config.yaml` where the apply phase reads it, and the task list is structured so that skipping it requires visibly checking `X.0` without having written anything. Neither is enforcement. The honest position is that this is a discipline held by the people doing the work, and the structure only makes lapses visible.

- **The MCP SDK for .NET is young and its API is still moving** → Confined to `Expenses.Mcp`, which contains no business logic. A breaking SDK change is a rewrite of tool declarations, not of the ledger. Pin the package version rather than floating it.

## Migration Plan

No existing system, no data, no rollback of user data to consider. Deployment order matters only in that provisioning must precede migration:

1. Provision the database with the required encoding and collation (D13). This cannot be undone in place.
2. Apply the initial EF migration: tables, unique index on `(occurred_at, amount)`, GIN trigram indexes, `unaccent` and `pg_trgm` extensions, `HasData` units.
3. Run category seeding (D15) — idempotent, safe to repeat.
4. Start `Expenses.Api` and `Expenses.Mcp`.

Rollback during development is dropping and recreating the database. Once real receipts exist, rollback means restoring a dump, since `bytea` content is not reproducible from anywhere else.

## Open Questions

These are deferrable without changing the specs, the approach, or the task breakdown:

- **Which real extraction engine** eventually replaces the placeholder — a vision model or a document-intelligence service. `IReceiptExtractor` is deliberately shaped around structured line output with per-field confidence, which both can satisfy.
- ~~**The confidence threshold** separating `Extracted` from `NeedsReview`.~~ **Answered by D20** — for numeric fields there is no threshold to choose, because correctness is decided arithmetically. A threshold remains only for descriptions, merchant names and category guesses, where it gates a review marker rather than the state of the extraction.

- **Basket-level discounts.** A loyalty coupon applied to a total rather than to a line cannot be expressed under D19, and forcing it into a line would break reconciliation. The same decision covers deposit returns, bag fees and rounding adjustments. Deferred deliberately, because it is really the question "may `Money` ever be negative", and D7 should not be reopened speculatively — it should be reopened by a receipt that needs it.

- **Whether to query a tax authority's receipt verification service.** Decoding a fiscal QR yields a verification URL, and for Montenegro that service exists. Not attempted in this change, for four reasons that would each need resolving first: verification expires ninety days after issuance, so it can never serve backfill; there is no documented public API, only a consumer web portal, so any integration is scraping; it is unconfirmed whether the portal returns line items at all rather than only totals and tax; and it is jurisdiction-specific in a way nothing else in this design is.

- **Which vision tiers fill stages 2 and 4.** D20 fixes the shape — cheap tier, then arithmetic, then expensive tier on failure — without naming models. At single-user volume the whole cascade is a rounding error annually, so the choice should be made on accuracy over multilingual thermal-print text, not on price.

- **How chain and branch are told apart during ingestion.** D18 models the relationship but does not decide who populates it. A receipt printing both a brand and a branch code gives an extractor enough to propose the pair; whether it does so automatically or leaves it to the user is a behaviour question that needs real extraction output to answer.
- **Initial category taxonomy** — which categories ship seeded and how deep the hierarchy goes. Content, not structure; changing it is editing seed data.
- **Whether the React client is a separate origin** (Vite dev server proxying, or served as static files by `Expenses.Api`). Only affects CORS configuration, and only once `FE/` has content.
- **Retention of unconfirmed extraction candidates.** Whether stale candidates are ever swept, and after how long. No behaviour depends on it yet.
