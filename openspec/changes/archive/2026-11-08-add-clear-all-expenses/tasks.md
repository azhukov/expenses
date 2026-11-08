# Tasks

## 1. Ports and error codes (exempt from test-first: signatures and constants only; behaviour is covered in 2.0 and 3.0)

- [x] 1.1 Add `Count` and `DeleteAllIfCountIs` to `IPurchaseRepository`, plus the `PurchaseClearOutcome` record (design D2). Give the EF implementation `throw new NotImplementedException()` bodies with their XML docs written. Implement both in the `InMemoryLedger` fake, which must also drop expenses and receipt keys. Verify with `dotnet build BE/Expenses.sln`.
- [x] 1.2 Add `purchase.clear.expected_count_required`, `purchase.clear.expected_count_invalid` and `purchase.clear.count_mismatch` to `ApplicationErrors`. Add `PurchaseService.Count` and `ClearAll(int? expectedCount)` as `NotImplementedException` stubs whose XML docs state D1's check order, and add the `ClearLedgerResult` DTO. Verify with `dotnet build BE/Expenses.sln`.

## 2. Count and clear use cases

- [x] 2.0 Write failing tests over fakes in a new `Expenses.Application.Tests/Purchases/ClearLedgerTests`, and run them to see each fail:
  - Counting: "Counting a populated ledger", "Counting an empty ledger".
  - Clearing: "Clearing a populated ledger" (list empty, get → `purchase.not_found`), "Reference data survives a clear" (categories, units, aliases including a learned one, merchants), "Clearing an empty ledger".
  - The guard: "No expected number is given" (code and field `expectedCount`), "A negative expected number", "The ledger changed since it was counted" (code `purchase.clear.count_mismatch`, message names 13, nothing deleted).
  - After a clear: "Re-recording a cleared purchase", "Candidates go with the cleared purchases", "Receipt files go with the cleared purchases" (fake image store), "Confirming a capture taken before a clear", "Scanning a cleared invoice again".
  - A file still shared by a purchase recorded after the clear is kept (D3).
  - A failing `images.Delete` for one key does not fail the clear, and the other keys are still deleted (D3).
- [x] 2.1 Implement `PurchaseService.Count` and `ClearAll` per design D1 and D3. Use the injected `IExtractionCandidateStore`; report files that could not be deleted in `ClearLedgerResult` (no logger in Application). Verify that every 2.0 test passes.

## 3. Persistence

- [x] 3.0 Write failing integration tests in a new `Expenses.Integration.Tests/Persistence/PurchaseClearStorageTests` against the Testcontainers database, and run them to see each fail:
  - `Count` across months.
  - `DeleteAllIfCountIs` removes `purchases` and their `expenses` rows, and returns the ids and distinct receipt storage keys.
  - A mismatch deletes nothing and returns the current count.
  - Merchants, categories, units and `unit_aliases` row counts are unchanged.
  - "An interrupted clear deletes nothing": cancel or fail the operation after the lock is taken and check every row remains.
  - A write in flight (inserted, not yet committed) when the clear starts is waited for and counted, so a clear expecting the old count is refused rather than deleting it unseen.
- [x] 3.1 Implement the EF `Count` and `DeleteAllIfCountIs`: a transaction, `LOCK TABLE purchases IN SHARE ROW EXCLUSIVE MODE`, count, read ids and keys, then `ExecuteDelete` (design D2). Verify that every 3.0 test passes.

## 4. HTTP and MCP

- [x] 4.0 Write failing tests, and run them to see each fail. The ones that clear live in a new `Expenses.Integration.Tests/Api/LedgerClearTests`, each on a database of its own, because clearing the shared suite database would delete what other classes arranged:
  - "Counting over HTTP" (`GET /purchases/count` → `{ count }`), "Clearing over HTTP" (200 `{ deleted }`), "A clear without an expected number over HTTP" (400, standard shape, field `expectedCount`), "A stale clear over HTTP" (409, standard shape, message names the current count).
  - "Clearing from an allowed browser origin" (DELETE preflight answered, response readable by the origin).
  - "Counting over MCP", asserted equal to the count over HTTP.
  - End to end: capture, confirm, clear over HTTP; the receipt file is gone from the permanent store, and a capture taken before the clear is still confirmable.
  - `Mcp/McpAdapterTests`: "Clearing is not offered over MCP" (`count_purchases` listed; no tool name contains `delete` or `clear`).
- [x] 4.1 Add `purchase.clear.count_mismatch` to the 409 list in `ErrorHandling`, and check that the two `expected_count` codes map to 400. Verify with the 4.0 status-code tests.
- [x] 4.2 Add `GET /purchases/count` and `DELETE /purchases` (binding `[FromQuery] int? expectedCount`) to `PurchasesController` per design D4, logging any `ReceiptFilesNotRemoved` at warning level. Verify that the HTTP and CORS tests from 4.0 pass.
- [x] 4.3 Add the `count_purchases` MCP tool to `PurchaseTools`, with a description saying it counts across all dates. Verify that the MCP and cross-adapter tests from 4.0 pass.

## 5. Browser client

- [x] 5.0 Write failing Vitest and Testing Library tests, and run them to see each fail:
  - `api/reads.test.ts`: `countPurchases` (URL, method).
  - `api/writes.test.ts`: `clearLedger(12)` (method `DELETE`, `expectedCount=12` in the query, parsed `{ deleted }`, `LedgerError` carrying the code on 409).
  - New `components/ClearLedgerDialog.test.tsx`: "The number to be deleted is shown" (12 across all dates, irreversibility stated), "Confirming clears the ledger" (request expects 12, purchases queries invalidated), "Cancelling changes nothing" (no request sent), "Nothing to clear", "The count cannot be read" (no confirm button), "Something was recorded meanwhile" (409 message shown, count refetched to 13, new confirmation required), "The ledger cannot be reached" (message shown, dialog stays).
  - `routes/Home.test.tsx`: "The control is reachable from home", "Activating the control deletes nothing", and after a successful clear the empty-ledger state and a zero month total are shown.
  - Capture is still the most prominent control: the `Home.test.tsx` prominence test now expects exactly three controls (capture plus two quiet tools), and the existing `Home.layout.test.tsx` touch-target test covers the new button.
- [x] 5.1 Add `countPurchases` to `api/reads.ts`, `clearLedger` to `api/writes.ts`, and `queryKeys.purchaseCount` with `usePurchaseCount({ enabled })` to `api/queries.ts`. Verify that the api tests from 5.0 pass.
- [x] 5.2 Add `components/ClearLedgerDialog.tsx` with its CSS module per design D6. Verify that the dialog tests from 5.0 pass.
- [x] 5.3 Add the "Clear all" button to the `tools` nav in `Home.tsx`, styled like the Units link, owning the dialog's open state. Verify that the Home tests from 5.0 pass.
- [x] 5.4 Run the `fe-style-fix` workflow (format, lint:fix, tsc, structure check) until it is clean. Verify that `npm run lint` and the structure check exit 0. (Done: lint, `tsc -b --force` and structure are clean, and every file this change touched is Prettier-clean. 34 untouched files are still flagged only for CRLF line endings from `core.autocrlf=true`; that predates this change and was left alone.)

## 6. Wrap-up (exempt from test-first: style, documentation and verification)

- [x] 6.1 Run the `be-editorconfig-fix` workflow until `dotnet build BE/Expenses.sln` is clean under `TreatWarningsAsErrors`.
- [x] 6.2 Run all three BE test projects and the FE suite (`./test-be.sh`, `./test-fe.sh`) and record the pass counts. (Domain 100; Application 231; Integration 233 passed, 1 skipped as manual-only; FE 241.)
- [x] 6.3 Update `BE/tests/SCENARIO-COVERAGE.md` with the new scenarios and where they are tested. Add clearing and the HTTP-only exception to `BE/Expenses.Mcp/README.md`, and a "Clearing the ledger" section to `BE/README.md` (it has no API section to extend). Verify that every scenario in this change's specs is listed.
- [x] 6.4 Manual check against the running app (`./up.sh`): record two purchases, use "Clear all", see the count of 2 and confirm, and check that the home screen is empty and the Units screen still lists its aliases.

## Workflow follow-up

- Sync the delta specs and archive the change once review is complete (`/opsx:archive`).
