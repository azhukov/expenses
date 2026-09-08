## Why

Today a purchase must already exist — with a known date, amount and reconciled expense lines — before a receipt image can be attached to it. A user who only has a photo of a receipt has no way to start: they must type out the purchase by hand first, which is exactly the data entry the receipt image was meant to replace. The extraction cascade already reads a date, amount, merchant and line items off a receipt (including a fiscal timestamp decoded from a QR code where present), so nothing about the domain actually requires the purchase to come first.

## What Changes

- **New capture flow**: an image can be submitted with no purchase behind it. The image is stored in a temporary, GUID-keyed area, extraction runs synchronously in the same request, and the caller gets the extraction result (candidate lines, amount, merchant, occurred-at) back immediately — nothing is written to the ledger yet.
- **New confirm flow**: the caller (having reviewed or edited the candidates) submits them together with the temporary key. The image is promoted into permanent, content-addressed storage, the temporary copy is discarded, and the purchase is created in one step with the receipt already attached and already `Extracted` — no separate attach step, no re-running extraction.
- **BREAKING**: removes the existing attach-image-to-an-existing-purchase flow (`AttachReceiptImage`, `POST /purchases/{id}/receipt`, and its MCP surface). A purchase can no longer acquire a receipt after the fact; every receipt now enters the ledger through capture-and-confirm, attached at the purchase's creation.
- **BREAKING**: removes the background extraction queue entirely. Extraction is no longer asynchronous: the `Pending`/`Extracting` states, the startup sweep that re-queues stranded work, and the in-process channel all go away. Extraction is a direct, synchronous call — made once at capture, and again (still synchronously) whenever extraction is explicitly re-run.
- Re-running extraction for an already-recorded purchase's receipt keeps working, but runs synchronously in the request rather than being queued.
- A daily background job deletes temporary-store files older than one day that were never confirmed into a purchase — the one piece of background processing this change keeps, unrelated to extraction.

## Capabilities

### New Capabilities

(none — this reshapes how receipt-ingestion capability enters the system rather than adding a new domain concept)

### Modified Capabilities

- `receipt-ingestion`: the image-to-existing-purchase attach flow is replaced by capture-then-confirm; the extraction lifecycle drops its asynchronous `Pending`/`Extracting` states and becomes synchronous; candidates for a not-yet-confirmed capture are held by the caller rather than server-side; a new temporary-storage and orphan-cleanup requirement is added.
- `api-surface`: HTTP and MCP surfaces both gain capture/confirm operations; the receipt-upload-is-HTTP-only requirement is replaced by a capture-and-confirm requirement that also clarifies MCP's continued inability to accept image bytes for capture.

## Impact

- **Domain**: `Receipt`'s extraction state machine loses `Pending`/`Extracting` as states extraction can sit in between requests; `Purchase.Record` gains an optional receipt reference supplied at creation.
- **Application**: `AttachReceiptImage`, `RunExtraction`'s queue-oriented shape, `IExtractionQueue` and its background drain are removed; `RecordPurchase` is extended to accept a temp-store key and receipt metadata; a new `CaptureReceipt` use case runs the cascade synchronously against unstored-purchase bytes; `RequeueExtraction` calls the cascade directly instead of enqueueing.
- **Infrastructure**: a new temporary receipt store (GUID-keyed, separate from the permanent content-addressed store) with a promote-to-permanent operation and a daily orphan sweep; `ExtractionQueue`/`ExtractionService` (`BackgroundService`) and the startup sweep are deleted.
- **Both adapters** (`Expenses.Api`, `Expenses.Mcp`): the attach endpoint/tool is removed; a capture endpoint/tool and a confirm endpoint/tool (or an extended record-purchase) are added.
