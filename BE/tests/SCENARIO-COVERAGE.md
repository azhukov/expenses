# Scenario coverage audit

Task 10.15 of `add-personal-expense-ledger`: every scenario in `openspec/changes/add-personal-expense-ledger/specs/**/spec.md`
that is directly executable has a test named after it, and every gap is either closed or recorded
here with a reason.

**156 scenarios** across four capability specs. **319 tests** across three projects, all green.

## Where each capability's scenarios are tested

| Capability | Scenarios | Where |
| --- | --- | --- |
| `purchase-recording` | 35 | `Expenses.Domain.Tests/PurchaseTests`, `ExpenseTests`; `Expenses.Application.Tests/Purchases/*`; `Expenses.Integration.Tests/Persistence/SchemaTests`, `Ledger/LedgerBehaviourTests` |
| `reference-data` | 43 | `Expenses.Domain.Tests/CategoryTests`, `UnitTests`, `MerchantTests`; `Expenses.Application.Tests/ReferenceData`, `Merchants`; `Expenses.Integration.Tests/Persistence/SeedingTests`, `RepositoryTests`, `Ledger/*` |
| `receipt-ingestion` | 50 | `Expenses.Domain.Tests/ReceiptTests`; `Expenses.Application.Tests/Receipts/*`, `Extraction/ExtractionCascadeTests`; `Expenses.Integration.Tests/Receipts`, `Extraction/*`, `Ledger/ReceiptJourneyTests` |
| `api-surface` | 44 | `Expenses.Integration.Tests/Api/HttpAdapterTests`, `CorsTests`, `HttpsTests`, `Mcp/McpAdapterTests`, `Ledger/CrossAdapterTests` |

Test classes carry a class-level XML doc naming the requirements they cover, and test methods are
named after the scenario they implement, so the mapping is readable from the test file rather than
only from this table.

## Gaps the audit found, and what was done

Seven scenarios had no test of their own when the audit ran. All seven are now covered by
`Expenses.Integration.Tests/Ledger/CoverageGapTests`:

| Scenario | Outcome |
| --- | --- |
| "Extraction state is readable" | **Was a real gap in the product, not only in the suite.** A retrieved purchase did not report the state of its image. `PurchaseView.ExtractionState` was added, test-first. |
| "Manual purchase has no image" | Covered: a manually recorded purchase reports no image and no extraction state. |
| "Non-Latin receipt text" | Covered: Cyrillic and Greek line text, unit text and category text round-trip character for character. |
| "Parent is assignable" | Covered: a category with children is assignable to an expense in its own right. |
| "Purchase references a chain directly" | Covered: the merchant equivalent of the same rule. |
| "User cannot create units" | Covered: no creation path exists over either interface — the audit asserts the absence rather than a refusal. |
| "Declared type disagrees with content" | Covered over HTTP: text declaring itself a JPEG is rejected by content sniffing. |

## Scenarios not directly executable, and why

| Scenario | Reason |
| --- | --- |
| "MCP without HTTP" / "HTTP without MCP" | Structural rather than behavioural: the two hosts are separate processes with no reference between them, asserted by `ProjectReferenceRulesTests`. Every MCP test runs with no API host in the process, and every HTTP test runs with no MCP host, which is the same claim demonstrated continuously. |
| "Decoding succeeds" on a photographed thermal receipt | Measured at zero hits over ~300 preprocessing combinations (D20). The hit path is exercised against a QR code the test renders; the real photograph is exercised by `DecoderRegressionTests`, which asserts the documented behaviour — the pipeline result is the same whatever the decoder makes of it — rather than a decode that the design says will not happen. |
| "Placeholder can produce each terminal state" | Covered as three separate configurations (`Reconciling`, `NonReconciling`, `LowConfidence`, `Failure`) rather than one test, because the states differ in what causes them. |

## Known limits of the current suite

- **Concurrency is tested with two writers, not many.** `Concurrent_duplicate_submissions` races two
  identical requests, which is the case D4 describes. It does not stress the guard under load.
- **The integration suite shares one database across test classes.** Classes therefore own distinct
  date ranges and tax numbers rather than truncating between tests. Two collisions were found and
  fixed this way during the change; the alternative — a database per class — costs several seconds
  each and was judged not worth it at this size.
- **Reporting is tested at the data level.** "A purchase referencing a branch rolls up to its parent
  chain" is asserted over the merchant relationship and a sum, because this change specifies no
  reporting feature to call.

## Test-first review (10.16)

Every behaviour in this change was preceded by a test that was run and seen to fail. Three lapses
are recorded here rather than papered over:

1. **The extraction queue and its startup sweep** (7.7, 7.8) were implemented before their tests.
   The tests were written afterwards and then verified by mutation: the sweep was disabled and
   `An_image_left_pending_by_a_restart_is_swept_up` failed; the drain was disabled and both queue
   tests failed. Both were restored and both tests pass.
2. **The fiscal decoder** (7.10) was likewise implemented before `FiscalDecodingTests`. Its first
   run was red for an unrelated reason — the ZXing ImageSharp binding targets an older ImageSharp —
   and the decoder was rewritten against ZXing's own luminance source with the tests failing, which
   restored the ordering for the code that now stands.
3. **Domain seams left as `NotImplementedException`** by section 2 — `Purchase.AttachReceipt`,
   the fiscal identifier members of `Receipt`, `ExtractionResult.From`,
   `ExtractionCandidate.Propose` — were filled in under section 3's tests, which were seen red
   against those exact members first.

Everything else followed red, green, refactor: section tests were written and run before any
implementation task in that section, and the failing run is recorded in the session that produced
it.

## Scenarios added by `require-purchase-entry-fields`

`purchase-recording`'s "Expense line detail" changed: a unit became required on every line and a
description gained a 200-character limit. The new scenarios and where they are tested:

| Scenario | Where |
| --- | --- |
| "A description at the limit is accepted" | `Expenses.Domain.Tests/ExpenseTests.A_description_at_the_limit_is_accepted` |
| "An over-long description is rejected" | `ExpenseTests.An_over_long_description_is_rejected`, and over the use case in `Expenses.Application.Tests/Purchases/RecordPurchaseTests.An_over_long_description_is_rejected` |
| "An expense without a unit is rejected" | `RecordPurchaseTests.An_expense_without_a_unit_is_rejected` and `.A_rejected_line_without_a_unit_is_named`; over HTTP in `Expenses.Integration.Tests/Api/HttpAdapterTests.An_expense_without_a_unit_is_rejected` |
| "A unit matched during extraction satisfies the rule" | `RecordPurchaseTests.A_unit_matched_during_extraction_satisfies_the_rule`; the refusal of the opposite case in `Expenses.Application.Tests/Receipts/CandidateTests.Confirming_a_candidate_that_matched_no_unit_is_rejected` |
| "Existing unit-less expenses stay readable" | Not executed as a scenario of its own: nothing in the suite writes a unit-less expense any more, and the rule lives above the entity, so a stored row is loaded by the same mapping as before. |

The line about the description limit being applied after trimming is not a spec scenario; it is
recorded as `ExpenseTests.The_description_limit_is_applied_after_trimming` because the limit is
otherwise ambiguous about padding.

Tests that record a line they do not care about now supply the seeded `PCS` unit, through a
`Piece` constant, a seeded unit in the test class's constructor, or the `Line` helper in
`HttpAdapterTests`. That is the fixture cost of the rule, not a weakening of those tests: each one
still asserts what it did before.

## Scenarios added by `add-qr-first-capture`

The fiscal invoice moved off the receipt image and onto the purchase, and a payload can now be
captured and confirmed with no image at all. The BE scenarios and where they are tested:

| Scenario | Where |
| --- | --- |
| "A purchase with a fiscal identity and no image", "A purchase with both", "Deleting the image keeps the fiscal identity", "No state without a receipt" | `Expenses.Domain.Tests/PurchaseReceiptTests`, which also pins the invariant that a state exists exactly when an image or invoice does |
| "Existing receipts keep their fiscal identity" | `Expenses.Integration.Tests/Persistence/FiscalInvoiceMigrationTests`: old-shape rows migrated forward. It fails if the migration's `UPDATE` of the source columns is removed; that was checked. |
| "A payload that yields an invoice", "The service returns no invoice", "A payload no identifier can be read from", "Nothing is held after a fiscal capture", "A payload captured on its own" | `Expenses.Application.Tests/Receipts/CaptureFiscalTests`; that the service is not asked, over the real portal client, in `Expenses.Integration.Tests/Api/FiscalCaptureTests.An_unparseable_payload` |
| "A confirmed fiscal capture becomes a whole purchase", "The date defaults from the payload", "A confirmation naming neither an image nor a payload" | `Expenses.Application.Tests/Purchases/RecordPurchaseTests`; the last also over HTTP and MCP |
| "Re-run with no image" | `Expenses.Application.Tests/Receipts/RerunExtractionTests.Re_run_with_no_image` |
| "A receipt scanned twice", "A duplicate can still be confirmed", "A new invoice", "No invoice identification code" | `Expenses.Application.Tests/Receipts/DuplicateInvoiceTests` (both capture paths, supplied, decoded and retrieved codes); the lookup over PostgreSQL in `Persistence/RepositoryTests`; both endpoints in `Api/FiscalCaptureTests.Reading_a_duplicate_warning_on_either_capture_endpoint` |
| api-surface: "Capturing a payload", "An oversized payload", "A missing payload", "An unparseable payload", "Fiscal capture over HTTP", "Reading a duplicate warning", "No duplicate", "A purchase with a fiscal identity and no image", "Requesting the image of an image-less purchase" | `Expenses.Integration.Tests/Api/FiscalCaptureTests` |
| api-surface: "MCP confirms a fiscal-only capture", "Confirming a fiscal-only capture over either interface" | `Mcp/McpAdapterTests.An_assistant_confirms_a_fiscal_only_capture_by_its_payload`, with the HTTP half in `FiscalCaptureTests.A_purchase_with_a_fiscal_identity_and_no_image` |

One behaviour change came out of writing these rather than out of the spec directly. A URL carrying
none of the verification parameters used to be parsed as a bare invoice code. It now yields no
identifiers: `Extraction/FiscalIdentityTests.An_address_that_is_not_a_verification_address_yields_no_identifiers`
(design D37).

Four existing assertions follow the `hasReceipt` → `hasReceiptImage` rename (D41). Each still asserts
what it did before, because every purchase in them either carries an image or carries nothing.

## Scenarios added by `add-unit-aliases`

Printed unit text is now matched to a unit through aliases, both during extraction and when a line
is saved, and an unknown spelling the user resolves is learned. Paths below are relative to `BE/tests`
unless they start with `FE/`.

| Scenario | Where |
| --- | --- |
| reference-data: "Spellings that differ only in form are one alias", "Inner characters are significant", "Text that normalises to nothing" | `Expenses.Application.Tests/ReferenceData/UnitTextTests` (the normaliser); "Text that normalises to nothing" also as a refusal in `UnitAliasManagementTests` and as a 400 in `Expenses.Integration.Tests/Api/HttpAdapterTests.Alias_rejections_carry_their_status_and_the_standard_error_shape` |
| reference-data: "The alias is stored normalised", "Adding an alias", "Adding an alias the unit already has", "Adding an alias to an unknown unit", "Adding an alias to a deactivated unit", "An alias may not repeat a unit code", "One alias, one unit", "A unit with several aliases", "Removing an alias", "Removing an alias from the wrong unit", "Removing an alias leaves history unchanged", "Units are listed with their aliases", "Adding an alias does not change the unit" | `Expenses.Application.Tests/ReferenceData/UnitAliasManagementTests`, asserting on error codes; an empty alias refused by the entity in `Expenses.Domain.Tests/UnitAliasTests` |
| reference-data: "One alias, one unit" (storage side) | `Expenses.Integration.Tests/Persistence/UnitAliasStorageTests.One_alias_one_unit`: the unique index refuses the row |
| reference-data: "Starting aliases are present", "A removed starting alias stays removed", "A user alias survives restart" | `Persistence/UnitAliasStorageTests`, each on a database of its own, with a restart being the real migrate-and-seed path |
| reference-data: "Unit codes match in normalised form", "An alias of an inactive unit does not match" | `Expenses.Application.Tests/Receipts/UnitMatchingTests` |
| receipt-ingestion: "A fiscal invoice's units are matched", "Vision-extracted units are matched", "A code match is preferred to an alias", "Unknown text leaves the line unmatched", "A re-run uses the aliases as they now stand" | `Receipts/UnitMatchingTests`, across `Capture`, `CaptureFiscal` and `RerunExtraction`; "A fiscal invoice's units are matched" also end to end against the stored invoice in `Expenses.Integration.Tests/Extraction/FiscalJourneyTests` |
| receipt-ingestion: "Unit text is preserved alongside a match" | `Expenses.Domain.Tests/ExtractionCandidateTests` |
| receipt-ingestion: "A receipt whose units all match can be confirmed unchanged"; purchase-recording: "Printed unit text alone satisfies the rule", "A supplied unit code overrides the printed text", "An expense without a unit is rejected" (now: unknown printed text and no code), "An unknown spelling is learned", "Learning applies to direct recording", "A known alias is not overridden", "A matched unit teaches nothing", "A code teaches nothing", "The earlier line decides within one save", "A rejected save teaches nothing", "History is not re-matched" | `Expenses.Application.Tests/Purchases/UnitLearningTests` |
| api-surface: "Listing units over HTTP carries aliases", "Adding an alias over HTTP", "Removing an alias over HTTP", "Aliases are addressed in normalised form", "Units cannot be created through alias operations" | `Expenses.Integration.Tests/Api/HttpAdapterTests`; the 409, 400 and 404 mappings in `.Alias_rejections_carry_their_status_and_the_standard_error_shape` |
| api-surface: "Listing units over MCP carries aliases", "Adding an alias over MCP", "Removing an alias over MCP", "Repeating an add is safe", and "Aliases are addressed in normalised form" over MCP | `Mcp/McpAdapterTests` |
| api-surface: "A conflicting alias is rejected the same way on both interfaces" | `Ledger/CrossAdapterTests` |
| browser-client: "The units screen lists aliases", "Adding an alias", "Removing an alias", "A conflicting alias is reported in place", "Aliases can be read when they cannot be changed" | `FE/src/routes/Units.test.tsx`; the requests themselves in `FE/src/api/writes.test.ts` |
| browser-client: "The units screen is reachable from home" | `FE/src/routes/Home.test.tsx` |
| browser-client: "A learned alias appears" | `FE/src/routes/Capture.test.tsx`: a confirmed capture invalidates the units query. That the server learned the alias is `UnitLearningTests.An_unknown_spelling_is_learned`. |

The alias tests in the integration suite share one database with every other test class. A test
that removes the starting alias `kd`, or adds `kut`, puts it back in a `finally`, so no other test
sees a different dictionary.

Several of these tests passed on their first run, because the code they cover went in before
them: the storage tests (the mapping and migration of tasks 2.x), the two listing tests and the
end-to-end fiscal match. Each was then seen failing against a deliberate break of that code and
passing again once it was restored:

| Break | Tests that failed |
| --- | --- |
| The migration's unique index on `alias` dropped | `One_alias_one_unit` |
| The starting `kom` row dropped | `Starting_aliases_are_present`, and `One_alias_one_unit`, which relies on it |
| Startup seeding re-inserting `st` and deleting `kut` | `A_removed_starting_alias_stays_removed`, `A_user_alias_survives_restart` |
| `UnitView` built with no aliases | `Listing_units_over_http_carries_aliases`, `Listing_units_over_mcp_carries_aliases` |
| Extraction matching switched off | `FiscalJourneyTests.A_fiscal_invoices_units_are_matched` |

At the end of the change: `Expenses.Domain.Tests` 100 passed; `Expenses.Application.Tests` 217
passed; `Expenses.Integration.Tests` 219 passed and 1 skipped (`ClaudeVisionGoldenPathTests`,
manual only because it calls the paid API); the FE suite 224 passed in 16 files.

## Scenarios added by `add-clear-all-expenses`

| Scenario | Where |
| --- | --- |
| purchase-recording: "Counting a populated ledger", "Counting an empty ledger", "Clearing a populated ledger", "Reference data survives a clear", "Clearing an empty ledger", "No expected number is given", "A negative expected number", "The ledger changed since it was counted", "Re-recording a cleared purchase" | `Expenses.Application.Tests/Purchases/ClearLedgerTests` |
| purchase-recording: "An interrupted clear deletes nothing"; storage side of "Clearing a populated ledger", "Reference data survives a clear", "The ledger changed since it was counted"; the lock (a write in flight is counted, not lost) | `Expenses.Integration.Tests/Persistence/PurchaseClearStorageTests`, each test on a database of its own |
| receipt-ingestion: "Receipt files go with the cleared purchases", "Candidates go with the cleared purchases", "Confirming a capture taken before a clear", "Scanning a cleared invoice again"; a file that cannot be deleted does not fail the clear | `ClearLedgerTests`; the first and third also end to end in `Expenses.Integration.Tests/Api/LedgerClearTests.Receipt_files_go_and_pending_captures_stay` |
| receipt-ingestion: "Interrupted file removal leaves no broken reference" | Not directly executable: the order (commit, then delete files) is what guarantees it, and `ClearLedgerTests.A_file_that_cannot_be_deleted_does_not_fail_the_clear` shows a failed removal leaves the ledger cleared and the file reported |
| api-surface: "Counting over HTTP", "Counting over MCP" (asserted equal to HTTP), "Clearing over HTTP", "A clear without an expected number over HTTP", "A stale clear over HTTP", "Clearing from an allowed browser origin" | `Expenses.Integration.Tests/Api/LedgerClearTests`, each test on a database of its own |
| api-surface: "Clearing is not offered over MCP" | `Mcp/McpAdapterTests.Clearing_is_not_offered_over_mcp` |
| browser-client: "The control is reachable from home", "Activating the control deletes nothing", "Confirming clears the ledger" (the home side: empty state and zero total), "Cancelling changes nothing" | `FE/src/routes/Home.test.tsx` |
| browser-client: "Nothing to clear", "The number to be deleted is shown", "Confirming clears the ledger", "Cancelling changes nothing", "The count cannot be read", "Something was recorded meanwhile", "The ledger cannot be reached" | `FE/src/components/ClearLedgerDialog.test.tsx`; the requests themselves in `FE/src/api/reads.test.ts` and `writes.test.ts` |

The clearing tests never touch the shared suite database. A clear there would delete what every
other class arranged, so each test provisions a database of its own with `ProvisionAnother`.

Every test was seen failing before its implementation existed: on `NotImplementedException` stubs
for the use cases and the repository, on 405/404 before the endpoints, and on the missing module,
functions and button in the client.

At the end of the change: `Expenses.Domain.Tests` 100 passed; `Expenses.Application.Tests` 231
passed; `Expenses.Integration.Tests` 233 passed and 1 skipped (`ClaudeVisionGoldenPathTests`,
manual only); the FE suite 241 passed in 17 files.

## Scenarios added by `add-csv-export`

| Scenario | Where |
| --- | --- |
| ledger-export: "Export everything", "Export one month", "Only a start is given", "Nothing in the range", "An export is not paged", "An inverted range is rejected" (same code, fields and message as the listing) | `Expenses.Application.Tests/Purchases/ExportLedgerTests` |
| ledger-export: "Export everything" (storage side), "The last day is included whole", a start alone and an end alone, more than a listing page returned whole | `Expenses.Integration.Tests/Persistence/PurchaseExportStorageTests`, each test on a database of its own |
| ledger-export: "A purchase with three lines", "Optional values that are absent" | `ExportLedgerTests` |
| ledger-export: "The header row" | `Expenses.Integration.Tests/Api/LedgerCsvTests.The_header_row`, and over HTTP in `LedgerExportTests.Exporting_everything_over_http` |
| ledger-export: "Matched references are named", "Unmatched references fall back to what was printed", "A deactivated merchant is still named" (category and unit too), "Neither a reference nor verbatim text" | `ExportLedgerTests` |
| ledger-export: "Oldest first", "Lines keep their recorded order"; purchases at the same moment in recorded order | `ExportLedgerTests`; the database ordering in `PurchaseExportStorageTests` |
| ledger-export: "Text with commas and quotes survives" (round trip through an independent RFC 4180 reader), "Non-Latin text survives", "Numbers use an invariant form" (under sr-Latn-ME, de-DE, fr-FR and en-US), "Quantities carry no trailing zeros", "Occurrence is written as recorded", "A purchase recorded by date alone" | `Expenses.Integration.Tests/Api/LedgerCsvTests` |
| api-surface: "Exporting everything over HTTP", "Exporting a range over HTTP", "An open-ended range is named for its open end", "An inverted range over HTTP", "A malformed date over HTTP", "Exporting from an allowed browser origin"; exporting changes nothing | `Expenses.Integration.Tests/Api/LedgerExportTests` |
| api-surface: "Exporting is not offered over MCP" | `Mcp/McpAdapterTests.Exporting_is_not_offered_over_mcp` |
| api-surface: "Same operation over either interface", "Same validation outcome", "Duplicate guard spans interfaces" (restated unchanged, because the parity requirement they belong to was modified) | `Expenses.Integration.Tests/Ledger/CrossAdapterTests`, unchanged and still passing |
| browser-client: "The control is reachable from home", "Activating the control downloads nothing", "Cancelling sends nothing" (the home side) | `FE/src/routes/Home.test.tsx` |
| browser-client: "The current month is the default", "Exporting a month", "Exporting everything", "Exporting a date range", "An incomplete range cannot be sent", "An inverted range is refused before sending", "An export in progress", "The file is the ledger's file", "The ledger rejects the export", "The ledger cannot be reached", "Cancelling sends nothing" | `FE/src/components/ExportDialog.test.tsx` |
| browser-client: "Files are named for what was chosen"; month ranges, leap years and the year boundary | `FE/src/month/month.test.ts`, the month label in `FE/src/format/format.test.ts` |
| browser-client: the request itself (path, range, `accept: text/csv`, bytes returned unaltered, errors in the ledger's words) | `FE/src/api/reads.test.ts`, `FE/src/api/client.test.ts` |

Every test was seen failing before its implementation existed: on `NotImplementedException` stubs
for the use case, the repository read and the CSV writer, on 404 before the endpoint, and on
"Not implemented" stubs in the client. The one exception is
`McpAdapterTests.Exporting_is_not_offered_over_mcp`, which asserts an absence and so passed from
the first run; like its clearing sibling, it is a guard against a tool being added later.

Two tests changed during the change, both because the first version specified something the system
could not do. The quantity test first expected `2.50` for `2.50m` ("as recorded"). The `quantity`
column stores three places whatever is supplied, so a stored `1` reads back as `1.000`. Quantities
are now written without trailing zeros, and the spec says so. The malformed-date test caught a 406:
`[Produces("text/csv")]` forced the binding error into CSV negotiation (design D4).

At the end of the change: `Expenses.Domain.Tests` 100 passed; `Expenses.Application.Tests` 249
passed; `Expenses.Integration.Tests` 261 passed and 1 skipped (`ClaudeVisionGoldenPathTests`,
manual only); the FE suite 272 passed in 18 files.
