## Context

See proposal.md - Why. The backend contract this wires into (from `add-receipt-first-capture`):

- `POST /receipts/capture` — multipart, one `file` part, optional `fiscalIkof`/`fiscalJikr` form fields. Returns `CaptureResult`: `tempKey`, `state` (Extracted/NeedsReview/Failed), `failureReason`, `result` (candidate lines, amount, merchant, provenance/confidence — null on Failed), `validation` (arithmetic checks), `supplied`/`extracted` fiscal identifiers, `fiscalSource`. Nothing is held server-side after this response.
- `POST /purchases` — existing endpoint, now additionally accepting `capture: CapturedReceiptRequest` (the temp key and the extraction outcome echoed back verbatim) alongside the usual `amount`, `expenses`, `merchant`, `occurredAt`. `occurredAt` may be omitted only when the capture's fiscal QR decoded a creation timestamp.

The FE today (`FE/src/`) has a read-only API layer (`api/reads.ts` + `read()` in `api/client.ts`, GET-only, JSON) and no write path at all. `routes/Capture.tsx` is a placeholder; `capture/CaptureControl.tsx` (the camera-opening control) is unaffected by this change and stays as-is.

## Goals / Non-Goals

**Goals:**
- Wire capture → upload → review/edit → confirm end to end, using the endpoints as they exist today.
- Let the user correct anything extraction got wrong (or produced nothing for) before it becomes a purchase.

**Non-Goals:**
- Re-running extraction from the client, or any UI for a purchase that already exists (both untouched by `add-receipt-first-capture`; out of scope here).
- Merchant creation/matching UI beyond what `RecordPurchase` already does server-side from free text (`MerchantRequest.Text`) — the review form submits raw merchant text, not an ID.
- Offline queuing or retrying an upload across a page reload. Losing the in-memory `File` on refresh is an accepted limitation (see Risks).

## Decisions

**Upload triggers on mount, guarded against a double-fire.** React 19 StrictMode (dev) and React Router's re-render on state changes both risk invoking an effect twice for the same file. A `useRef` keyed on the file identity (not just a plain `useEffect` dependency) guards the mutation so the same image is never submitted twice for one visit to `/capture`. Alternative considered: require a manual "upload" tap — rejected, since the proposal's whole point is removing a now-pointless extra step; every user of this screen already just aimed a camera at a receipt.

**The review form is fed by, and holds, plain component state — no form library.** The FE already avoids codegen and heavier dependencies where three fields don't need one (D14 in `api/types.ts`); the review form is one screen with a handful of fields and a variable-length line list, well within `useState` + array operations. A form library would be the first one in the project for a form this small.

**A new `api/writes.ts` (or extending `client.ts`) carries the two write calls.** `captureReceipt(file, fiscal?)` posts `FormData` (no JSON body — the existing `read()` always sends/expects JSON, so it isn't reused for this call). `recordPurchase(command)` posts JSON and reuses `client.ts`'s error handling (`LedgerError`, `ErrorResponse` parsing) the way `read()` does, just for `POST` instead of `GET`. Both surface failures as the same `LedgerError` the rest of the client already uses — no second error vocabulary.

**The capture result is carried forward verbatim, in local state, exactly as the API requires.** `CapturedReceiptRequest` must echo `tempKey`, `state`, `failureReason` and the fiscal fields back unchanged (the server holds nothing to compare against). The review screen keeps the original `CaptureResult` untouched in state alongside the user's edits to the date/amount/lines, and builds the confirm request from both without ever re-deriving the echoed fields from what the user edited.

**Category and unit are plain `<select>`s from the existing dictionaries, addressed by code.** `useCategories`/`useMerchants` already exist; this change adds `useUnits` (`GET /units`, mirroring the other two reference-data hooks) so a line's unit can be picked the same way. A candidate's `categoryId`/`unitId` (already matched by the extractor) is resolved to its code for the initial selection; `categoryRaw`/`unitRaw` stay attached to the line and travel through unchanged, per the existing "verbatim text beside the match" rule the rest of the client already follows for merchants.

**Navigating away from `/capture` does nothing to the temporary capture.** The daily server-side sweep (already implemented) is the only cleanup; the client never calls anything to abandon a capture explicitly, matching the new spec requirement.

**On confirm success, the purchases query is invalidated rather than manually patched.** `useQueryClient().invalidateQueries({ queryKey: ['purchases', ...] })` before navigating back to `/`, consistent with the existing `refetchOnWindowFocus` posture in `App.tsx` — simplest way to guarantee Home reflects the new purchase without hand-rolling a cache update.

## Risks / Trade-offs

- **Refreshing `/capture` loses the in-memory `File`** (it lives only in router state) → the screen already falls back to "No photograph was handed over" for a missing file (existing `Capture.tsx` behaviour); this change keeps that fallback and does not attempt to persist the file across a reload.
- **A slow fiscal-portal-backed extraction leaves the user waiting on an unbounded request** → out of scope for this change (the backend change already made this synchronous by design); the waiting state at least makes the delay visible rather than looking hung. No client-side timeout is added.
- **Two mutations in sequence (capture, then confirm) means a user could background the tab mid-upload on iOS Safari** → not specifically handled; a failed/interrupted capture surfaces as the existing upload-failure state with a retry, since the original `File` is still held in state.
