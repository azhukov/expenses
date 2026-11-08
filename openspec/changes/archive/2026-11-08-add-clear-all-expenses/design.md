# Design

## Context

See proposal.md for why. The state this design works from:

- No use case deletes a purchase. The only deletion is `ReceiptService.Delete`, which detaches a receipt,
  discards candidates, saves, and only then removes the file if `CountByReceiptStorageKey` is zero (the
  sharing rule, D11 of the ledger design).
- `purchases` is the only aggregate root. Expense lines live in `expenses` with a foreign key that
  cascades on delete. The receipt reference (`receipt_storage_key`, …) and the fiscal identity are owned
  types split into columns on the `purchases` row. Merchants, categories and units are referenced with
  `Restrict`, so deleting purchases never reaches them, and nothing references a purchase.
- Extraction candidates are held only in memory (`InMemoryExtractionCandidateStore`), keyed by purchase id.
- Temporary captures live in `ITemporaryReceiptStore`, unrelated to any purchase until confirmed, and are
  swept daily by `OrphanCaptureSweep`.
- `ErrorHandling` maps application error codes to status codes: `*.not_found` → 404, a listed set → 409,
  the rest → 400, all in one error shape (code, message, fields).
- CORS already allows `DELETE` preflights from configured origins.
- The home screen loads only the current month (`usePurchases(currentMonthRange())`), so it cannot tell
  how many purchases the whole ledger holds.

## Goals / Non-Goals

**Goals:**

- One atomic clear: either every purchase that existed when the clear ran is gone, or none is.
- A clear that cannot be triggered by a bare request, and that refuses when the ledger moved since the
  user saw the number.
- Receipt files disposed of with the same order and sharing rule as a single receipt deletion.

**Non-Goals:**

- Deleting a single purchase, or deleting by date range. Clearing does not add either.
- Undo, soft delete, backup or export before clearing.
- Clearing reference data, or resetting identifier sequences. New purchases continue with higher ids.
- Authentication. The ledger has none today. The expected-count guard prevents accidents, not misuse.
- Removing temporary captures. The daily sweep keeps owning them.

## Decisions

### D1 — The use cases live in `PurchaseService`

`PurchaseService` gains `Count()` and `ClearAll(int? expectedCount)`. It already holds the purchase
repository and `IReceiptImageStore`; it gains `IExtractionCandidateStore`. `ClearAll`
validates the argument before it touches storage:

1. `expectedCount` is null → `purchase.clear.expected_count_required` (400, field `expectedCount`).
2. `expectedCount < 0` → `purchase.clear.expected_count_invalid` (400, field `expectedCount`).
3. Ask the repository to delete everything if the count matches (D2).
4. Mismatch → `purchase.clear.count_mismatch` (409). The message states the current count:
   "The ledger holds 13 purchases, not 12. Nothing was deleted."
5. On success, discard candidates for every deleted id, then dispose of receipt files (D3), and return
   `ClearLedgerResult(int Deleted, IReadOnlyList<string> ReceiptFilesNotRemoved)`.

The rule (a clear needs an expected count, and it must match) sits in Application, so neither adapter
carries it. The 409 code is added to `ErrorHandling`'s conflict list.

*Alternative considered:* a separate `LedgerService`. Rejected because there is one aggregate, and every
other purchase use case is already in `PurchaseService`.

### D2 — The repository performs a conditional bulk delete in one transaction

`IPurchaseRepository` gains:

- `Task<int> Count(CancellationToken)`.
- `Task<PurchaseClearOutcome> DeleteAllIfCountIs(int expected, CancellationToken)`, where
  `PurchaseClearOutcome(int CountBefore, bool Deleted, IReadOnlyList<long> PurchaseIds,
  IReadOnlyList<string> ReceiptStorageKeys)`.

The EF implementation opens a transaction, takes `LOCK TABLE purchases IN SHARE ROW EXCLUSIVE MODE` so
that no purchase can be inserted or deleted between the count and the delete (reads still proceed),
counts, and rolls back with `Deleted = false` on a mismatch. Otherwise it reads the ids and the distinct
non-null receipt storage keys, runs `ExecuteDelete` on `purchases` (the database cascades `expenses`),
and commits.

This is the one use case that does not go through `IUnitOfWork.SaveChanges` (D16). The transaction is
still one per use case, but it is owned by the repository, because `ExecuteDelete` bypasses the change
tracker and the lock has to cover the count.

*Alternatives considered:*

- Loading every purchase with its expenses and calling `Remove` before one `SaveChanges`. This keeps
  D16 literally, but it materialises the whole ledger to delete it, and a purchase inserted after the
  load would survive with no conflict reported.
- `SERIALIZABLE` isolation instead of a table lock. It is correct too, but it surfaces a race as a
  serialization failure that would need its own retry or mapping. The explicit lock makes a concurrent
  `Record` wait for the clear to finish instead.

### D3 — Receipt files are removed after commit, best-effort, under the sharing rule

After the delete commits, for each storage key returned, `PurchaseService` calls
`CountByReceiptStorageKey`. If the count is zero, it calls `images.Delete`. This keeps the existing order
(the reference goes first, then the file), so an interruption can only leave an unreferenced file. The
re-count protects a file that a purchase recorded right after the clear has started to share again.

A failure to delete one file is caught, its key is added to `ReceiptFilesNotRemoved`, and the loop
continues. It does not turn the clear into an error, because the ledger has been cleared and a leftover
file is safe to discard. This differs on purpose from the single-receipt delete, where the caller can
simply retry. The Application project carries no package references, logging included, so the HTTP
controller logs those keys at warning level. The HTTP response does not include them.

### D4 — HTTP shapes

- `GET /purchases/count` → 200 `{ "count": 12 }`. The literal segment cannot collide with
  `GET /purchases/{id:long}`.
- `DELETE /purchases?expectedCount=12` → 200 `{ "deleted": 12 }`. The response is 200, not 204, because
  the number deleted is the confirmation the caller reads back. The expected count travels as a query
  parameter, the same as `DELETE /units/{code}/aliases?alias=…`, because many proxies and clients drop or
  reject DELETE bodies.
- The controller binds `expectedCount` as `int?` and passes it through unchanged, so a missing value is
  reported by the Application code from D1 rather than by model binding. A non-numeric value fails model
  binding and is reported in the existing validation shape.

### D5 — MCP gets `count_purchases`, and no clear tool

`count_purchases()` returns `{ count }` through `PurchaseService.Count`. No tool calls `ClearAll`.
`McpAdapterTests` gains an assertion that no listed tool name contains `delete` or `clear`, and the parity
exception is named in the api-surface spec. That way the exception is a decision, not an omission.

*Alternative considered:* an MCP clear tool behind the same guard. Rejected at the user's direction. An
assistant can read a count and then pass it straight back, so the guard does not stop an assistant that
decides to clear.

### D6 — Client: a quiet tool and an in-page confirmation

- `api/reads.ts` gains `countPurchases()`. `api/writes.ts` gains `clearLedger(expectedCount)`.
  `api/queries.ts` gains `queryKeys.allPurchases` (the `['purchases']` prefix every month shares),
  `queryKeys.purchaseCount` and `usePurchaseCount()`. The hook takes no `enabled` flag: the dialog
  is mounted only while open, so the count is read only then.
- `components/ClearLedgerDialog.tsx` (rank 2) renders a modal confirmation with `role="alertdialog"`,
  `aria-modal`, a labelled heading and focus moved to Cancel. It fetches the count only when opened, with
  `staleTime: 0`. It states the count, that categories, units and merchants are kept, and that the
  deletion cannot be undone. With a count of 0 it says there is nothing to clear and renders no confirm
  button. If the count cannot be read, it reports that and renders no confirm button.
- Confirming calls `clearLedger(count)` with local pending and failure state, the same pattern as
  `Units.tsx`, rather than `useMutation`. On success it invalidates every `purchases` query and
  `purchaseCount`, then closes. On a `LedgerError` with code
  `purchase.clear.count_mismatch` it shows the ledger's message, refetches the count and waits for a new
  confirmation. Other failures show the message in place, and the dialog stays open.
- `Home.tsx` adds a "Clear all" button to the existing `tools` nav, styled like the Units link, and owns
  the dialog's open state.

*Alternative considered:* `window.confirm`. Rejected because it cannot show a count that is loaded after
the user taps, it cannot report a conflict in place, and it is awkward to test under jsdom.

## Risks / Trade-offs

- **[Irreversible data loss]** → The confirmation shows the exact number across all dates and says the
  deletion cannot be undone. The server refuses a request without the number, and refuses one where the
  number is stale. Neither stops a deliberate user; that is intended.
- **[Unauthenticated endpoint]** → Anyone who can reach the HTTP interface from an allowed origin, or with
  no origin, can count and then clear. This is the same exposure every write already has. It is
  acceptable for a personal ledger, and it is a reason to add authentication before any shared
  deployment.
- **[Table lock blocks writes for the length of the clear]** → A concurrent `Record` waits. A personal
  ledger holds thousands of rows at most, and the delete is a single cascaded statement, so the wait is
  short.
- **[Leftover files after a partial file cleanup]** → They are unreferenced and logged, and are safe to
  delete by hand. No sweep for them is added.
- **[Candidates for a deleted id]** → They are discarded by id. Because ids are never reused, a missed
  discard could only leak memory until restart, and could never attach to a new purchase.

## Migration Plan

No schema migration. Deploy the backend and frontend together as usual. Clearing deletes data and
cannot be rolled back. Rolling back the code removes the endpoint and the button.
