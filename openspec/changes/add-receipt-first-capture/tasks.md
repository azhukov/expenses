## 1. Domain: terminal-only extraction states

- [x] 1.0 Write failing `Expenses.Domain.Tests` cases for `Receipt`: a receipt can only be constructed already in a terminal state (Extracted, NeedsReview, Failed); `Pending` and `Extracting` are no longer valid `TransitionTo` targets; re-extracting an existing terminal receipt transitions directly to a new terminal state in one call.
- [x] 1.1 Remove `Pending` and `Extracting` from `Receipt.ExtractionState`; update `Receipt`'s factory/transition methods so a `Receipt` is always constructed in a terminal state and re-extraction is a single terminal-to-terminal transition.
- [x] 1.2 Update any domain-level XML doc comments and D-numbered references in `Receipt.cs` that describe the old async lifecycle.

## 2. Infrastructure: temporary receipt store

- [x] 2.0 Write failing `Expenses.Integration.Tests` (or unit tests, if the store needs no real filesystem fixture beyond a temp directory) covering: `Save(bytes)` returns a fresh GUID key on every call, including for identical bytes twice; `Read(key)` returns the bytes back, or null for an unknown key; `Delete(key)` removes the file and is safe to call on an already-missing key; `ListOlderThan(cutoff)` returns only entries whose write time is older than the cutoff.
- [x] 2.1 Implement `ITemporaryReceiptStore` in `Expenses.Application.Abstractions` (port) and a filesystem-backed implementation in `Expenses.Infrastructure` under its own configured root, separate from `ReceiptFileStore`'s root.
- [x] 2.2 Add a `Verify()` startup check for the temporary store's root, mirroring `ReceiptFileStore.Verify`, and wire it into `AddExpensesInfrastructure`.

## 3. Application: capture runs extraction synchronously with no purchase

- [x] 3.0 Write failing `Expenses.Application.Tests` cases for `CaptureReceipt` covering the receipt-ingestion spec scenarios "Capturing an image with nothing else known", "A temporary key is never reused", "State on capture", "Successful extraction", "Extraction that does not add up", "Low confidence on a value arithmetic cannot check", and "Failed extraction" (adapted to the capture path — no purchase exists).
- [x] 3.1 Implement `CaptureReceipt`: validates the image (reusing the existing format/size checks), saves bytes to the temporary store, runs `ExtractionCascade.Run` directly (no queue), and returns the `ExtractionResult` plus the temporary key. Persists nothing to the database.

## 4. Application: confirming a capture creates the purchase

- [x] 4.0 Write failing `Expenses.Application.Tests` cases for the extended `RecordPurchase` covering receipt-ingestion spec scenarios "A confirmed capture becomes a whole purchase", "Confirmation that does not reconcile", "Confirming an unknown or expired key", "Confirming promotes the temporary image", and the date-defaulting scenarios "Date taken from a decoded fiscal receipt", "Caller's date overrides the receipt", "No date anywhere".
- [x] 4.1 Extend `RecordPurchaseCommand` with an optional temp-key-and-extraction-outcome argument; when present, `RecordPurchase.Execute` reads the temp file, promotes it via the existing `IReceiptImageStore.Save`, deletes the temp file, resolves the occurred-at date (supplied date, else the fiscal `CreatedAt`, else reject), and attaches the resulting `Receipt` (already in its terminal extraction state) within the same `Purchase.Record(...)` call before the single `SaveChanges`.
- [x] 4.2 Ensure a confirmation that fails reconciliation leaves the temporary capture untouched (still confirmable again) and creates no purchase — no partial writes.

## 5. Application: re-run becomes synchronous

- [x] 5.0 Write failing `Expenses.Application.Tests` cases for the new synchronous `RerunExtraction` covering receipt-ingestion spec scenarios "Re-run replaces candidates", "Re-run after confirmation", and "Re-run a failed extraction" (all adapted to complete within one call, with no observable `Pending`/`Extracting` state).
- [x] 5.1 Replace `RequeueExtraction` with a synchronous `RerunExtraction` that loads the purchase's receipt bytes, runs `ExtractionCascade.Run` directly, replaces held candidates via `IExtractionCandidateStore.Replace`, and saves the new terminal state — all within the one call, no `Enqueue`.

## 6. Remove the background extraction queue

- [x] 6.0 No new behaviour is introduced by this removal — no test-first task applies. Existing tests referencing `IExtractionQueue`, `ExtractionQueue`, or `ExtractionService` are deleted or rewritten against the synchronous replacements from sections 3 and 5.
- [x] 6.1 Delete `IExtractionQueue`, `ExtractionQueue`, `ExtractionService` (the `BackgroundService`, including its startup sweep), and their DI registration in `AddExpensesInfrastructure`.
- [x] 6.2 Delete the old `RunExtraction` use case (superseded by `CaptureReceipt` and `RerunExtraction`) and `AttachReceiptImage`.

## 7. Data migration for stranded receipts

- [x] 7.0 No new runtime behaviour — no test-first task applies; this is a one-time data migration exercised by an EF Core migration test if the project has a pattern for that, otherwise verified manually per the migration plan.
- [x] 7.1 Write an EF Core migration that transitions any persisted `Purchase.Receipt` with `extraction_state` `Pending` or `Extracting` to `Failed`, recording a failure reason noting it was stranded by the removal of background extraction, before the `Pending`/`Extracting` enum values are removed from the mapping.

## 8. Infrastructure: daily orphan sweep

- [x] 8.0 Write failing tests for the sweep job covering receipt-ingestion spec scenarios "An old, unconfirmed capture is swept away", "A recent capture survives a cleanup run", and "Confirming after cleanup already removed it" (the latter as an `Expenses.Application.Tests` case on `RecordPurchase`'s "unknown key" handling, since the sweep itself has already been exercised via `ListOlderThan`/`Delete` in task 2.0).
- [x] 8.1 Implement a daily hosted job that lists and deletes temporary-store entries older than one day, and wire it into `AddExpensesInfrastructure`.

## 9. HTTP adapter

- [x] 9.0 Write failing `Expenses.Integration.Tests` (or the project's existing HTTP-adapter test style) for api-surface spec scenarios "Capture over HTTP" and covering the new confirm endpoint's request/response shape.
- [x] 9.1 Add `POST /receipts/capture` (multipart, no purchase id) calling `CaptureReceipt`, replacing `Upload` on `ReceiptsController`.
- [x] 9.2 Extend `POST /purchases` (or add the equivalent record-purchase route) to accept the optional temp key and receipt fields, calling the extended `RecordPurchase`.
- [x] 9.3 Update `RequeueExtraction`'s route to call the synchronous `RerunExtraction` and return its full result rather than just the `ReceiptView`.
- [x] 9.4 Remove the old `POST /purchases/{id}/receipt` upload route.

## 10. MCP adapter

- [x] 10.0 Write failing tests for api-surface spec scenarios "MCP confirms a capture" and "Confirming a capture over MCP".
- [x] 10.1 Add a `confirm_capture` MCP tool calling the extended `RecordPurchase`.
- [x] 10.2 Update `rerun_extraction` to call the synchronous `RerunExtraction` and return its full result in one round trip.
- [x] 10.3 Confirm no MCP tool for capture is added (capture stays HTTP-only, per api-surface spec's "Receipt capture is HTTP-only").

## 11. Documentation

- [x] 11.0 No new behaviour — no test-first task applies.
- [x] 11.1 Update `BE/ARCHITECTURE.md` and any D-numbered decision it references to describe the synchronous capture/confirm flow in place of the queue-based attach flow.
