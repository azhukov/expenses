## Why

The backend's capture-and-confirm flow (`add-receipt-first-capture`) is complete: `POST /receipts/capture` extracts a receipt synchronously and returns candidate lines, and `POST /purchases` accepts a temporary key to create the purchase in one step. The browser client stops short of using either. `Home` already hands the captured file to `/capture` (D1, `browser-client` spec), but `Capture.tsx` is a placeholder that renders the file's name and does nothing else — it was built only to prove the navigation hand-off works end to end. A user who photographs a receipt today sees their filename and nothing more; nothing is uploaded, nothing is reviewed, no purchase is ever created.

## What Changes

- **Capture uploads on arrival**: the moment `/capture` receives a file, it is submitted to `POST /receipts/capture`. The screen shows a waiting state while the fiscal-portal-backed extraction runs, since the request does not return until extraction reaches a terminal state.
- **New review screen**: on a successful response, the extracted candidate lines, amount, merchant and occurred-at date are shown for the user to review, and to edit before confirming — the API never reconstructs what the caller doesn't resubmit, so the client must carry the capture result forward itself.
- **Reconciliation and validation surfaced before submission**: where extraction reports `NeedsReview` (arithmetic mismatch, low-confidence value, or disagreeing fiscal identifiers) or `Failed`, the screen states why, and still lets the user enter or correct lines by hand rather than dead-ending.
- **New confirm action**: submits the temporary key, the capture's extraction outcome (echoed back unchanged, since the server holds nothing), and the user's final date/amount/lines to `POST /purchases`, creating the purchase with its receipt already attached.
- **Success and failure handling**: a confirmed purchase returns the user to Home showing the new entry; a rejected confirmation (reconciliation mismatch, missing date) is reported in place, with the capture still available to retry.
- **Abandoning a capture**: leaving `/capture` without confirming leaves no trace — the temporary capture is swept up by the existing daily server-side cleanup, and the client makes no attempt to delete it explicitly.

No backend change is required; this only wires the client to endpoints that already exist.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `browser-client`: adds requirements for the capture screen itself — uploading on arrival, showing the extraction wait, reviewing and editing candidates, submitting confirmation, and handling both extraction and confirmation failure — none of which the current spec covers (it only reaches the hand-off into `/capture`).

## Impact

- **FE**: `src/routes/Capture.tsx` gains real behaviour; likely new components for the candidate review form and new API functions (`captureReceipt`, an extended `recordPurchase` accepting a `Capture` field) alongside `src/api/reads.ts`'s existing read-only surface. `src/api/types.ts` gains the capture/extraction response shapes.
- **No backend, database or API contract changes** — `POST /receipts/capture` and the extended `POST /purchases` are already implemented and specified in `add-receipt-first-capture`.
