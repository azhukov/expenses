> **Test first.** Every section that produces behaviour begins at `X.0` with the tests
> for that section. No implementation task may be started while its section's `X.0` is
> unchecked, and each test must be run and seen to **fail for the expected reason**
> before the code that satisfies it is written. Test cases come from the WHEN/THEN
> scenarios in `specs/**/spec.md`; name each test after the scenario it implements.
> Sections 1, 5 and 11 produce no behaviour of their own and are exempt where noted.
> See D21.

## 1. Solution and project scaffolding

- [ ] 1.1 Create `BE/Expenses.sln` targeting .NET 10, with `Directory.Build.props` enabling nullable reference types, implicit usings and treat-warnings-as-errors
- [ ] 1.2 Create `Expenses.Domain` as a class library with no project or package references
- [ ] 1.3 Create `Expenses.Application` referencing only `Expenses.Domain`
- [ ] 1.4 Create `Expenses.Infrastructure` referencing only `Expenses.Application`
- [ ] 1.5 Create `Expenses.Api` (ASP.NET Core) referencing `Application` and `Infrastructure`
- [ ] 1.6 Create `Expenses.Mcp` (worker host) referencing `Application` and `Infrastructure`, with no reference to `Expenses.Api`
- [ ] 1.7 Create `tests/Expenses.Domain.Tests`, `tests/Expenses.Application.Tests`, `tests/Expenses.Integration.Tests`
- [ ] 1.8 Add an architecture test asserting the reference rules of D1: `Domain` depends on nothing, `Application` only on `Domain`, and neither adapter references the other
- [ ] 1.9 Add a `.gitignore` covering .NET and Node output, and confirm `FE/` remains an empty placeholder
- [ ] 1.10 Confirm `dotnet test` runs the three test projects from a clean checkout and reports zero tests rather than erroring, so the red-green loop is usable from task 2.0 onward
- [ ] 1.11 Exempt from test-first per D21 — this section produces no behaviour

## 2. Domain model

- [ ] 2.0 **Write the failing tests for this section** in `Expenses.Domain.Tests`, one per scenario, and run them: `Money` non-negativity and four-decimal precision; `Quantity` defaulting to 1 and three-decimal fractions; the reconciliation invariant including the exact-not-approximate case and the error carrying both values; occurrence normalisation to 00:00:00 with no timezone conversion; category and merchant cycle rejection; extraction state transitions including invalid ones; discount and list price set together or not at all, negative discount rejected, `Amount` never recomputed; null discount distinguishable from a zero discount; the derived purchase saving; and each arithmetic check reporting passed, failed or not applicable with the disagreeing values. Confirm every one fails before writing any of 2.1–2.15.
- [ ] 2.1 Implement `Money` as a `readonly record struct` over `decimal`, rejecting negative values, per D7
- [ ] 2.2 Implement `Quantity` with a default of 1, rejecting negative values, supporting three decimal places
- [ ] 2.3 Implement the `Expense` entity with description, quantity, amount, and optional unit, unit price, category, plus `category_raw` and `unit_raw` verbatim text
- [ ] 2.4 Implement the `Purchase` aggregate root holding occurrence, amount and its expenses, rejecting construction with zero expenses
- [ ] 2.5 Enforce the reconciliation invariant inside `Purchase` — the sum of expense amounts must equal the purchase amount exactly — with an error carrying both values
- [ ] 2.6 Implement occurrence normalisation so a date with no time becomes 00:00:00, with no timezone conversion, per D5
- [ ] 2.7 Implement `Category` with code, name, optional parent, `is_system` and `is_active`, rejecting parent assignments that create a cycle
- [ ] 2.8 Implement `Unit` with code, name, symbol, kind and `is_active`
- [ ] 2.9 Implement `ReceiptImage` with content hash, content type, size and extraction state
- [ ] 2.10 Implement the extraction state machine over Pending, Extracting, Extracted, NeedsReview and Failed, rejecting invalid transitions
- [ ] 2.11 Confirm the tests from 2.0 covering the reconciliation invariant, occurrence normalisation, quantity defaulting, cycle rejection and state transitions are now green, and refactor with them passing
- [ ] 2.12 Add `ListUnitPrice` and `DiscountAmount` to `Expense` as optional descriptive values set together or not at all, rejecting a negative discount and never recomputing `Amount` from them, per D19
- [ ] 2.13 Implement the derived purchase saving as the sum of line discount amounts, computed rather than stored, and derive the discount percentage for display only
- [ ] 2.14 Implement `Merchant` with name, optional tax identification number, optional parent and `is_active`, rejecting parent assignments that create a cycle, per D18
- [ ] 2.15 Implement the arithmetic validator as pure domain logic over an extraction result — line sum against total, list less discount against line amount, total against tax — reporting each check as passed, failed or not applicable with the disagreeing values, per D20
- [ ] 2.16 Confirm the validator tests from 2.0 are green, and extend them with the worked example in D20 and a result with one digit altered in each checked position — each new case written and seen to fail first
- [ ] 2.17 Verify no test in this section passed on its first run; any that did is treated as broken until it has been made to fail

## 3. Application layer — ports and use cases

- [ ] 3.0 **Write the failing tests for this section** in `Expenses.Application.Tests` against in-memory fakes, one per scenario, and run them: both duplicate-guard branches including the unique-violation re-query; a duplicate submission leaving expenses untouched; date-range listing order and paging; category creation, rename, deactivation with active children, and system-entry deletion; second-image rejection and content-hash reuse; candidate confirm, discard and re-run including confirmation that does not reconcile; deactivated category or unit assignment rejected; merchant resolution by tax number, by name fallback, and creation when unknown; merchant plays no part in the duplicate guard; and every cascade branch — optional stage misses, validation passes, validation fails then the fallback passes, both fail and the better result is kept, fiscal sources disagree. Confirm every one fails before writing any of 3.1–3.19.
- [ ] 3.1 Define ports: `IPurchaseRepository`, `ICategoryRepository`, `IUnitRepository`, `IReceiptImageStore`, `IExtractionCandidateStore`, `IReceiptExtractor`, `IClock`, `IUnitOfWork`
- [ ] 3.2 Implement `RecordPurchase`, returning a result that distinguishes a newly created purchase from an already-recorded one, per D3
- [ ] 3.3 Implement the duplicate guard flow in `RecordPurchase`: query by `(OccurredAt, Amount)` first, insert otherwise, and on unique violation re-query and return the winner, per D4
- [ ] 3.4 Ensure a duplicate submission returns the existing purchase unchanged and never appends or replaces its expenses
- [ ] 3.5 Implement `GetPurchase` and `ListPurchases` with date-range filtering, most-recent-first ordering and paging
- [ ] 3.6 Implement `ListCategories` and `ListUnits` with an option to include inactive entries and a stable display order
- [ ] 3.7 Implement `CreateCategory`, `RenameCategory` and `DeactivateCategory`, rejecting code changes, deletion of system entries, and deactivation of a category with active children
- [ ] 3.8 Implement `AttachReceiptImage`, rejecting a second image on a purchase that already has one, and reusing a stored image when the content hash already exists
- [ ] 3.9 Implement `GetExtractionCandidates`, `ConfirmCandidates` and `DiscardCandidates`, with confirmation applying the reconciliation invariant and replacing expenses in one transaction
- [ ] 3.10 Implement `RequeueExtraction` covering re-running a completed, failed or already-confirmed extraction, without altering confirmed expenses
- [ ] 3.11 Reject assignment of a deactivated category or unit to a new expense
- [ ] 3.12 Define the application error model — a stable error code, message and offending fields — shared by both adapters
- [ ] 3.13 Confirm the use-case tests from 3.0 are green, including both duplicate-guard branches, and refactor with them passing
- [ ] 3.14 Define the merchant port `IMerchantRepository` and implement `ResolveMerchant` — match by tax identification number where present, fall back to name, create when unknown, and report which happened
- [ ] 3.15 Implement `ListMerchants`, `SearchMerchants`, `RenameMerchant`, `SetMerchantParent` and `DeactivateMerchant`, rejecting cycles and deactivation of a merchant with active children
- [ ] 3.16 Extend `RecordPurchase` to resolve and attach a merchant while always retaining `merchant_raw`, and confirm the merchant plays no part in the duplicate guard
- [ ] 3.17 Define the cascade ports — an ordered `IExtractionStage` sequence, `IFiscalCodeDecoder`, and the extraction result carrying per-value stage provenance — per D20
- [ ] 3.18 Implement the cascade orchestrator: run free stages first, validate, run the fallback stage only on a failed validation, keep the better of two failed results, and set `Extracted` or `NeedsReview` from the validation outcome rather than from a reported score
- [ ] 3.19 Implement fiscal identifier capture and corroboration, retaining both values and entering `NeedsReview` when two sources disagree
- [ ] 3.20 Confirm the cascade tests from 3.0 are green for every branch, and verify each was seen red first

## 4. Persistence and schema

- [ ] 4.0 **Write the failing integration tests for this section** in `Expenses.Integration.Tests`, and run them: `numeric` precision round-tripping for amounts, unit prices, quantities and discounts; `occurred_at` returning `DateTimeKind.Unspecified` unchanged; the unique index on `(occurred_at, amount)` rejecting a second insert; content-hash uniqueness; a null discount and a zero discount round-tripping as distinct states; and a merchant with a null tax number coexisting with one that has a tax number. These need the Testcontainers harness from 10.1, so bring that forward and expect them to stay red longer than the domain tests — per D21, that is not a reason to defer writing them.
- [ ] 4.1 Add `Npgsql.EntityFrameworkCore.PostgreSQL` and implement `ExpensesDbContext` in `Infrastructure`
- [ ] 4.2 Configure entity mappings with the column types from D10, mapping `Money` as a complex type onto a single `numeric(19,4)` column
- [ ] 4.3 Map `occurred_at` as `timestamp without time zone` and confirm no `DateTimeKind` conversion occurs on read or write
- [ ] 4.4 Configure `bigint GENERATED ALWAYS AS IDENTITY` keys, foreign keys, and length `CHECK` constraints on text columns
- [ ] 4.5 Configure the unique index on `purchases (occurred_at, amount)` and the unique index on `receipt_images (content_hash)`
- [ ] 4.6 Declare the `unaccent` and `pg_trgm` extensions via `HasPostgresExtension`, and add GIN trigram indexes on `expenses.description`, `merchants.name` and `purchases.merchant_raw`, per D14
- [ ] 4.7 Add `HasData` seeding for `units`, per D15
- [ ] 4.8 Implement `IDesignTimeDbContextFactory<ExpensesDbContext>` so `dotnet ef` works without a startup project
- [ ] 4.9 Generate the initial EF migration and verify the emitted SQL matches D10 exactly — the generated migration file itself is exempt from test-first per D21; the schema behaviour it produces is not, and is covered by 4.0
- [ ] 4.10 Implement the repository adapters, keeping `ReceiptImage` content behind a separate query so bytes are never loaded incidentally
- [ ] 4.11 Translate the unique-index violation into the already-recorded outcome rather than letting it surface as an infrastructure exception
- [ ] 4.12 Implement `AddExpensesInfrastructure(configuration)` as the single composition entry point used by both hosts
- [ ] 4.13 Add the `merchants` table with `tax_id` unique-when-present, self-referencing `parent_id` and `is_active`, and the nullable `purchases.merchant_id` and `purchases.merchant_raw` columns, per D10 and D18
- [ ] 4.14 Add the nullable `expenses.list_unit_price` and `expenses.discount_amount` columns as `numeric(19,4)`, per D19
- [ ] 4.15 Add the nullable fiscal identifier columns on `receipt_images`, imposing no format, plus the per-value stage provenance carried on extraction results
- [ ] 4.16 Confirm the persistence tests from 4.0 are green, including null versus zero discount, and refactor with them passing

## 5. Database provisioning

- [ ] 5.0 **Write the failing test for the one behaviour in this section** — the startup check of 5.3 — asserting that a database provisioned with the wrong encoding or collation fails loudly at startup, and run it before writing the check. Tasks 5.1, 5.2, 5.4 and 5.5 are exempt per D21: they produce configuration and documentation, not behaviour.
- [ ] 5.1 Write the `CREATE DATABASE` provisioning script with UTF8 encoding, ICU locale provider and `und` locale, per D13
- [ ] 5.2 Add `docker-compose.yml` for local PostgreSQL mounting the script into `docker-entrypoint-initdb.d`
- [ ] 5.3 Implement a startup check asserting the database encoding and collation, failing loudly on mismatch
- [ ] 5.4 Enable migrate-on-startup for local development only, and produce a migration bundle for other environments
- [ ] 5.5 Document in `BE/README.md` that encoding and collation cannot be changed after creation and are not managed by EF

## 6. Reference data seeding

- [ ] 6.0 **Write the failing tests for this section** and run them: first run against an empty database produces the seeded entries; a repeated run duplicates nothing; a user rename survives a re-run; a user-created category survives a re-run; and the merchant dictionary is empty after seeding while categories and units are not.
- [ ] 6.1 Implement category seeding via `UseAsyncSeeding`, upserting by `code` so user renames and user-created categories survive
- [ ] 6.2 Define the initial category taxonomy as seed data
- [ ] 6.3 Confirm the seeding tests from 6.0 are green, and refactor with them passing

## 7. Receipt storage and extraction

- [ ] 7.0 **Write the failing tests for this section** and run them: content-hash reuse on identical bytes and a new row for a re-photographed receipt; content-type detection from content with a declared type that disagrees; the 15 MB limit rejecting before bytes are buffered; placeholder determinism across two runs of the same image; placeholder-produced candidates identifying their engine and stage; the decoder-miss path leaving state, user-facing output and later stages unchanged; stage provenance recorded per value; and the deliberately non-reconciling placeholder result driving a real validation failure and the fallback. Confirm every one fails before writing any of 7.1–7.15.
- [ ] 7.1 Implement the `bytea`-backed `IReceiptImageStore`, computing the SHA-256 content hash on write and reusing an existing image on hash match
- [ ] 7.2 Implement content-type detection from file content rather than the declared type or extension, accepting JPEG, PNG, WebP, HEIC and PDF
- [ ] 7.3 Enforce the 15 MB upload limit at the adapter boundary before bytes are buffered
- [ ] 7.4 Implement `IExtractionCandidateStore` with candidates held separately from expenses, per D12
- [ ] 7.5 Implement `PlaceholderReceiptExtractor` producing deterministic output derived from the image hash, recording engine name and version on every result
- [ ] 7.6 Add placeholder configuration to simulate low-confidence and failure outcomes so `NeedsReview` and `Failed` are exercised
- [ ] 7.7 Implement the bounded `Channel` queue and `BackgroundService` that drains it, keeping extraction off the request path
- [ ] 7.8 Implement the startup sweep that re-queues images left in Pending after a restart
- [ ] 7.9 Make the confidence threshold configurable with a documented placeholder default, applying only to values arithmetic cannot check — descriptions, merchant names, category and unit guesses — per D20
- [ ] 7.10 Implement `IFiscalCodeDecoder` over a QR library, decoding from stored image bytes with a bounded preprocessing ladder and a hard time budget
- [ ] 7.11 Make decoding failure an ordinary outcome: no state change, no user-facing error, no effect on later stages — satisfying the decoder-miss test from 7.0, which must have been seen red against a decoder that throws
- [ ] 7.12 Register the ordered stage sequence, configuring the placeholder twice as the cheap and expensive tiers so the cascade shape is exercised before a real engine exists
- [ ] 7.13 Record which stages ran and which stage produced each value on every extraction result, alongside the existing engine name and version
- [ ] 7.14 Wire the domain arithmetic validator into the cascade between the two vision tiers
- [ ] 7.15 Add placeholder configuration producing a deliberately non-reconciling result, so a failed validation and the fallback path are exercised for a real reason rather than a simulated score

## 8. HTTP adapter

- [ ] 8.0 **Write the failing adapter tests for this section** and run them: 201 for a new purchase and 200 for an already-recorded one; the single error shape carrying code, message and offending fields on every validation failure; not-found in that same shape; a generic failure carrying a correlation identifier and no stack trace; multipart upload and image download with the correct content type; merchant and discount round-tripping with the derived saving; fiscal identifiers readable before extraction has run; a submitted discount percentage rejected; and per-stage provenance with each arithmetic check reported.
- [ ] 8.1 Implement purchase endpoints — create, get by id, list by date range — returning 201 for a new purchase and 200 for an already-recorded one, per D3
- [ ] 8.2 Implement multipart receipt upload and image download with the correct content type
- [ ] 8.3 Implement extraction endpoints — read candidates, confirm, discard, re-run
- [ ] 8.4 Implement reference data endpoints — list categories and units, create, rename and deactivate a category
- [ ] 8.5 Implement the single error response shape carrying error code, message and offending fields, mapped from the application error model
- [ ] 8.6 Implement generic failure handling returning a correlation identifier with no stack trace or internal detail
- [ ] 8.7 Add OpenAPI generation so the React client has a contract to work from — exempt from test-first per D21, being generated output
- [ ] 8.8 Verify no business rule or validation lives in the adapter beyond request shape binding
- [ ] 8.9 Accept a merchant when recording a purchase, and return the merchant, the verbatim merchant text, line discounts and the derived saving when retrieving one
- [ ] 8.10 Implement merchant endpoints — list, search, rename, set parent, deactivate
- [ ] 8.11 Accept fiscal identifiers as fields alongside the multipart upload, readable before extraction has run
- [ ] 8.12 Report per-stage provenance, each arithmetic check with its outcome, and fiscal identifier corroboration state on extraction responses
- [ ] 8.13 Reject a submitted discount percentage, accepting only a discount amount

## 9. MCP adapter

- [ ] 9.0 **Write the failing tool tests for this section** and run them: tools discoverable with their argument schemas; recording a purchase returning the stored purchase and identifier; a repeated identical call succeeding and stating the purchase was already recorded; a genuine validation failure still reported as an error; reference data resolved by code and a display name rejected; image bytes rejected as a tool argument; a newly added merchant reported as newly added; and arithmetic check outcomes and corroboration state readable through the extraction tools.
- [ ] 9.1 Add the .NET MCP server SDK with a pinned package version, per the risk noted in design
- [ ] 9.2 Implement stdio transport, and HTTP transport for a hosted deployment
- [ ] 9.3 Implement tools for recording a purchase, retrieving a purchase, and listing purchases over a date range
- [ ] 9.4 Implement tools for listing reference data, addressing categories and units by `code` and rejecting display names
- [ ] 9.5 Implement tools for reading extraction state and candidates, and for re-running extraction against a stored image
- [ ] 9.6 Ensure image bytes are never accepted as tool arguments
- [ ] 9.7 Write tool descriptions and argument schemas stating what each tool does and when to use it
- [ ] 9.8 Return a successful, clearly worded result on repeated recording rather than an error, and verify genuine validation failures are still errors
- [ ] 9.9 Verify the MCP host runs with the HTTP host stopped
- [ ] 9.10 Implement merchant tools — list and search — and accept a merchant when recording a purchase, stating in the result whether the merchant was newly added
- [ ] 9.11 Report arithmetic check outcomes and fiscal identifier corroboration through the extraction tools, in wording an assistant can act on

## 10. Integration suite against real PostgreSQL

> This is not a later testing phase. Each test below is written before the feature it
> exercises, as part of that feature's `X.0` task; this section is where they live and
> where the harness they need is built. Task 10.1 is a prerequisite of 4.0 and should
> be done first.

- [ ] 10.1 Set up Testcontainers PostgreSQL using the same provisioning as production, including encoding and collation — harness only, exempt from test-first per D21, and required before 4.0 can run red
- [ ] 10.2 Test the concurrent duplicate submission case, asserting exactly one purchase and two successful results
- [ ] 10.3 Test `numeric` precision round-tripping for amounts, unit prices and quantities
- [ ] 10.4 Test ICU collation ordering across mixed-language content
- [ ] 10.5 Test trigram search tolerance of accents and character-level typos
- [ ] 10.6 Test the full receipt path — upload, queued extraction, candidates, confirm, re-run — through the HTTP adapter
- [ ] 10.7 Test that the same operations produce identical results through the HTTP and MCP adapters
- [ ] 10.8 Test that a purchase created over one adapter is matched by the duplicate guard when submitted over the other
- [ ] 10.9 Test merchant resolution end to end — same tax number under a different spelling resolves to one merchant, same name under different tax numbers stays two, and a merchant with no tax number falls back to name matching
- [ ] 10.10 Test that a purchase referencing a branch rolls up to its parent chain in reporting
- [ ] 10.11 Test the cascade end to end — validation passes, validation fails and the fallback runs, both fail, and an optional stage misses
- [ ] 10.12 Test fiscal identifier disagreement between an upload-supplied value and an extracted one, asserting both are retained and the image enters `NeedsReview`
- [ ] 10.13 Test trigram merchant search over both `merchants.name` and `purchases.merchant_raw`, including an accent-stripped query
- [ ] 10.14 Add a decoder regression test using a real photographed receipt, asserting the documented behaviour — whatever the outcome, the pipeline result is unchanged — rather than asserting a successful decode
- [ ] 10.15 Audit the suite against the spec scenarios: every scenario in `specs/**/spec.md` that is directly executable has a test named after it, and every gap is either closed or recorded with a reason
- [ ] 10.16 Verify by review that no test in the change was written after the code it covers, and that none passed on its first run

## 11. Developer setup

- [ ] 11.1 Write `BE/README.md` covering local run, database provisioning, migration commands and the design-time factory
- [ ] 11.2 Document the placeholder extractor, how to simulate its failure modes, and what changes when a real engine replaces it
- [ ] 11.3 Document how to connect an MCP client to the stdio host for local use
- [ ] 11.4 Document the cascade in `BE/README.md` — the stage order, which stages are real in this change, how to configure the tiers, and why validation replaces a reported confidence score
- [ ] 11.6 Document the test-first discipline in `BE/README.md` — the red-green-refactor loop, that spec scenarios are the source of test cases, which task groups are exempt and why, and that the slow integration suite does not excuse writing its tests late
- [ ] 11.7 Exempt from test-first per D21 — this section produces documentation
- [ ] 11.5 Record the measured QR decoding result from D20 so the next person does not re-derive it, and note that client-side decoding at capture is the intended primary path once `FE/` exists
