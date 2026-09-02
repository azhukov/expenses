## Context

Today a receipt image can only be attached to a purchase that already exists, via `AttachReceiptImage` (`Receipts.cs`), which queues extraction on `IExtractionQueue` for a `BackgroundService` (`ExtractionService`, `ExtractionQueue.cs`) to run later. `Purchase.Record` requires an occurred-at date, an amount and reconciled expense lines all at once (D2, D6) — there is no way to create a purchase from an image alone, since none of those are known from a photo until extraction has run.

The extraction cascade (`ExtractionCascade.Run`, `ExtractionCascade.cs`) is already a synchronous, self-contained `Task` that takes bytes in and an `ExtractionResult` out; it has no dependency on a persisted purchase. The only thing coupling it to the queue is its caller, `RunExtraction`, which loads a purchase, flips `Receipt.ExtractionState`, and saves — that's queue-worthy because it mutates persisted state, not because the cascade itself is slow enough to require deferral. The fiscal-QR stage already decodes an invoice creation timestamp into `FiscalIdentifiers.CreatedAt` (`FiscalTrail` in `ExtractionCascade.cs`); nothing currently reads that value onto a purchase's `OccurredAt`.

See `proposal.md` for why this needs to change.

## Goals / Non-Goals

**Goals:**
- Let a caller start from an image alone: capture bytes, get an extraction result back, then create a purchase from it in one step.
- Remove the background extraction queue entirely — extraction is always a direct, synchronous call from here on, for both capture and re-run.
- Keep every existing invariant on `Purchase` untouched: it is still created whole, with amount reconciling to its lines, in one transaction.
- Keep the permanent receipt store's content-addressing and deduplication exactly as they are today; only add a temporary staging area in front of it.

**Non-Goals:**
- Async/202 responses for capture. Latency is accepted for now; the design leaves room for a later 202-style HTTP variant but does not build one.
- A generic "draft purchase" concept in the domain. Nothing about `Purchase` changes; the temporary state lives entirely outside it, as a file plus a caller-held result.
- Persisting capture results server-side in any form (in-memory or otherwise). Between capture and confirm, the only server-side state is the temporary file.
- OCR/vision changes. The cascade's stages are unchanged; only who calls them and when is different.

## Decisions

### A temporary store, GUID-keyed, separate from the permanent one

**Decision**: add a small second store, `ITemporaryReceiptStore` (or similar), with `Save(bytes) -> key` (a fresh `Guid` per call, no deduplication), `Read(key) -> bytes?`, `Delete(key)`, and `ListOlderThan(cutoff) -> keys` for the sweep. It is a distinct root directory from the permanent `ReceiptFileStore`, not a subdirectory of it, so the two are never confused by a naive directory walk.

**Why GUID instead of content hash**: content-addressing exists to deduplicate permanent storage; a temporary file is deleted within a day regardless of whether its bytes match another temporary file, so there is nothing to gain from hashing it — and a GUID sidesteps a subtle bug the hash approach invites: two different in-flight captures of the *same* photo must not be able to collide and step on each other's temp file while one is still being confirmed.

**Alternatives considered**: reusing `ReceiptFileStore` with a `pending/` prefix inside the same root — rejected because the permanent store's `Verify()` at startup and its dedup logic both assume every file under its root is a confirmed, referenced receipt; carving out an exception inside it is more invasive than a second small store.

### Promotion is read-then-`Save`, not a filesystem move

**Decision**: confirming a capture reads the temp file's bytes and calls the existing `IReceiptImageStore.Save(bytes)` — the same method `AttachReceiptImage` used today — then deletes the temp file. This is not a bare filesystem rename, because the permanent store's path is content-addressed (`sha256(bytes)`), which is only known once the bytes are hashed, and `Save` already contains the correct dedup-and-write-atomically logic. Re-deriving that logic for a move-based path would duplicate it for no benefit; a capture image is small enough (≤15MB, same limit as today) that reading it into memory once more is not a meaningful cost.

**Alternatives considered**: computing the hash at capture time and using it as both the temp key and a hint for promotion — rejected per the GUID decision above (temp identity should not be tied to content), and it buys nothing since `Save` has to re-verify the content type by sniffing anyway.

### Capture and confirm are two requests; nothing is cached in between

**Decision**: capture returns the full `ExtractionResult` (candidates, amount, merchant, fiscal identifiers including any decoded `CreatedAt`) in its response body. Confirm requires the caller to resubmit the date, amount, lines and merchant explicitly — it does not look anything up by the temp key except the image bytes themselves. This was chosen directly over the alternative (an in-memory store keyed by the temp key, mirroring today's `IExtractionCandidateStore`) because it needs no new state, no expiry logic beyond the file sweep, and no "candidates lost, image not" edge case to define — if the process restarts between capture and confirm, the only casualty is the temp file itself, cleaned up the same way an abandoned one is.

**Trade-off accepted**: a client must hold and correctly echo back a result it received earlier, including any edits, rather than the server remembering it. For an assistant-driven client (the primary MCP use case) this is a natural fit — the assistant already holds the conversation state. A thin HTTP client would need to round-trip the same JSON it received, which is ordinary REST practice.

### `RecordPurchase` gains an optional receipt attachment; no new use case for the common path

**Decision**: extend `RecordPurchase`'s command with an optional `(tempKey, extractionState, fiscalIdentifiers)` — when present, the use case promotes the image (see above) and attaches the resulting `Receipt` to the `Purchase` in the same `Purchase.Record(...)` call, before the one `SaveChanges`. This keeps "a purchase is created whole, in one transaction" true without exception: today that means amount+lines together; from now on it also means +receipt, when a capture is behind it. A `CaptureReceipt` use case is added for the capture side (bytes in, `ExtractionResult` out, nothing persisted but the temp file); it does not touch `Purchase` at all.

**Why not a separate `ConfirmCapture` use case**: `ConfirmCandidates` today edits an *existing* purchase's expenses from held candidates. Confirming a capture instead *creates* a purchase — that is `RecordPurchase`'s job, not `ConfirmCandidates`'s, and giving `RecordPurchase` an optional receipt argument is a smaller change than teaching a second use case how to create a purchase.

### Removing the queue removes `Pending`/`Extracting` as states, not just as a mechanism

**Decision**: `Receipt.ExtractionState` drops the `Pending` and `Extracting` members entirely; a receipt is only ever constructed already in a terminal state (`Extracted`, `NeedsReview`, `Failed`). `RunExtraction`'s queue-shaped orchestration is deleted; what remains of it (load bytes, run cascade, interpret outcome) is inlined into `CaptureReceipt` (no purchase yet) and a synchronous `RerunExtraction` (existing purchase, replaces `RequeueExtraction`). `IExtractionQueue`, `ExtractionQueue`, `ExtractionService` (the `BackgroundService` and its startup sweep) are deleted outright — nothing enqueues once nothing needs deferring.

**Why not keep the states but stop using two of them**: an enum member nothing can ever legitimately hold is worse than not having it — every switch/pattern match over `ExtractionState` would need a "this never happens" branch forever. Removing them is a straightforward, low-risk cut since nothing external observes a receipt mid-extraction anymore (the whole point of going synchronous).

### The daily orphan sweep is a plain timed job, not a queue

**Decision**: a `BackgroundService` (or hosted timer) that wakes once a day, lists temp-store entries older than 24h via `ListOlderThan`, and deletes them. This is the one piece of background infrastructure this change keeps — explicitly not extraction-related, and with no per-item retry or ordering requirements, unlike the extraction queue it replaces.

## Risks / Trade-offs

- **[Risk] Capture request latency** is now bounded only by the cascade's own worst case (deterministic stages + the 5s-capped fiscal-portal call + the vision fallback tier). → *Mitigation*: none built now (accepted per proposal); the temp-store/GUID-key design already leaves the door open to a later `202 Accepted` + poll-by-key variant on HTTP without changing the confirm contract, since confirm only ever needed the key and the caller's own data.
- **[Risk] A caller loses or mis-edits the capture result before confirming**, since nothing is held server-side. → *Mitigation*: this is the same class of risk `ConfirmCandidates` already accepts today when an `edited` list is supplied instead of held candidates; not a new failure mode, just the *only* path now instead of one of two.
- **[Risk] Removing `Pending`/`Extracting` is a breaking change to anything reading `Receipt.ExtractionState`** (persisted data, if any receipts are currently in those states). → *Mitigation*: a migration step converts any row found `Pending`/`Extracting` to `Failed` with a note that it was stranded by this change, since there is no bytes-in-flight to resume synchronously for a receipt that was mid-queue at deploy time.
- **[Trade-off] Two file stores instead of one** adds a small amount of operational surface (two roots to back up, two `Verify()` checks at startup) in exchange for keeping the permanent store's invariants (content-addressed, every file referenced) simple and untouched.

## Migration Plan

1. Add the temporary store and its `Verify()` startup check alongside the existing permanent one.
2. Add `CaptureReceipt`, extend `RecordPurchase`, add the synchronous `RerunExtraction` replacing `RequeueExtraction`'s body.
3. Add the capture and confirm endpoints/tools; remove `AttachReceiptImage` and its endpoint/tool.
4. Data migration: any `Purchase.Receipt` persisted with `ExtractionState` `Pending` or `Extracting` is transitioned to `Failed` (reason: "stranded by removal of background extraction").
5. Remove `IExtractionQueue`, `ExtractionQueue`, `ExtractionService`, and the `Pending`/`Extracting` enum members.
6. Add the daily orphan-sweep hosted service for the temporary store.

No feature flag: this is a single deploy, since the old and new attach mechanisms cannot coexist once `Purchase.Receipt` is only ever set at creation.
