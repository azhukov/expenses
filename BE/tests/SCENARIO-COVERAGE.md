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
| `api-surface` | 28 | `Expenses.Integration.Tests/Api/HttpAdapterTests`, `Mcp/McpAdapterTests`, `Ledger/CrossAdapterTests` |

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
