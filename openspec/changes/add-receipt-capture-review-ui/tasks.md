## 1. API types and write client

- [x] 1.0 Write failing `api/client.test.ts` cases for a new `send`/`postJson` helper: it posts a JSON body and parses a success response the same way `read()` does; it maps a non-OK `ErrorResponse` body to `LedgerError` with `code`/`status`/`correlationId`; it maps an unreachable request to a `LedgerError` with no code, mirroring `read()`'s existing coverage.
- [x] 1.1 Add the JSON `POST` helper to `api/client.ts`, reusing `isErrorResponse` and `LedgerError` rather than duplicating them.
- [x] 1.2 Add `CaptureResult`, `ExtractionResultView`, `ExtractionCandidateView`, `ArithmeticValidationReport`/`ArithmeticCheck`, `FiscalIdentifiers`, `FiscalSource` and `UnitView` to `api/types.ts`, matching the backend's `CaptureResult`/`ExtractionResultView`/`ExtractionCandidateView`/`ArithmeticValidationReport`/`UnitView` shapes (camelCase, enums as names).
- [x] 1.3 Write failing `api/writes.test.ts` cases for `captureReceipt(file, fiscal?)`: it posts multipart form data with a `file` part (and `fiscalIkof`/`fiscalJikr` fields when supplied) to `/receipts/capture` and returns the parsed `CaptureResult`; a non-OK response raises `LedgerError` the same way `read()` does.
- [x] 1.4 Implement `captureReceipt` in `api/writes.ts` using `fetch` directly (multipart cannot go through the JSON helper), reusing `LedgerError`/`isErrorResponse` for error handling.
- [x] 1.5 Write failing `api/writes.test.ts` cases for `recordPurchase(command)`: it posts JSON matching `RecordPurchaseRequest` (including an optional `capture` field shaped like `CapturedReceiptRequest`) to `/purchases`, and returns the parsed `RecordPurchaseResponse`-shaped view for both 200 (already recorded) and 201 (created) responses.
- [x] 1.6 Implement `recordPurchase` in `api/writes.ts` using the JSON `POST` helper from 1.1.
- [x] 1.7 Write failing `api/reads.test.ts` case for `listUnits()`: it reads `/units` and returns `UnitView[]`.
- [x] 1.8 Implement `listUnits` in `api/reads.ts`, and a `useUnits` hook in `api/queries.ts` mirroring `useCategories`.

## 2. Upload on arrival

- [x] 2.0 Write failing `routes/Capture.test.tsx` cases for the browser-client scenarios "Upload starts without a further tap" and "The wait is visible": given a file in router state, the screen calls `captureReceipt` once without further interaction, and shows a loading indicator with no candidate/amount/date content while the call is outstanding.
- [x] 2.1 Write a failing case for "The upload cannot be reached": when `captureReceipt` rejects, the screen reports the failure and offers a retry that re-submits the same `File` object.
- [x] 2.2 Write a failing case guarding against a double submission: re-rendering the component with the same file (simulating a StrictMode-style double effect invocation) still calls `captureReceipt` exactly once.
- [x] 2.3 Implement the upload-on-arrival behaviour in `routes/Capture.tsx`: trigger `captureReceipt` on mount via a ref-guarded effect keyed on the file, track loading/error/result state, and wire the retry action.

## 3. Review and edit candidates

- [x] 3.0 Write failing tests for the browser-client scenarios "Candidates are shown for review" and "Candidates are editable": given a capture result in Extracted or NeedsReview state, the review form renders the candidate lines, amount, merchant and occurred-at date, and lets the user edit a line's description/amount/quantity/category, add a line, and remove a line.
- [x] 3.1 Write a failing test for "The capture result is not re-requested": editing candidates and confirming submits the original response's `tempKey`/`state`/fiscal fields unchanged, regardless of what the user edited in the lines/amount/date.
- [x] 3.2 Implement a `CaptureReview` component (or extend `Capture.tsx`) holding the original `CaptureResult` and the user's editable date/amount/lines as separate state, rendering the line list with add/remove, and category/unit `<select>`s sourced from `useCategories`/`useUnits`.

## 4. Extraction problems surfaced

- [x] 4.0 Write failing tests for "An arithmetic mismatch is named", "A low-confidence value is flagged" and "Disagreeing fiscal identifiers are shown": given a NeedsReview result with each condition present in `validation`/`reportedConfidence`/fiscal fields, the review screen states the specific reason, and confirming remains possible.
- [x] 4.1 Implement the NeedsReview messaging in the review component, reading `validation.checks`, each candidate's `reportedConfidence`, and the capture result's fiscal corroboration fields.
- [x] 4.2 Write failing tests for "Failure is explained" and "A manually entered capture can still be confirmed": given a Failed result, the screen shows the failure reason and empty entry fields, and confirming with hand-entered data submits the same as an extracted capture.
- [x] 4.3 Implement the Failed-state branch: empty editable fields seeded instead of candidates, same confirm path as 3.2/5.x.

## 5. Confirm

- [x] 5.0 Write failing tests for "Confirmation creates a purchase" and "The date defaults from the receipt": confirming calls `recordPurchase` with the built request, invalidates the purchases query, and navigates to `/`; when the capture established a fiscal creation timestamp and the user left the date field untouched, no date is sent explicitly (or the resolved one is sent, per whichever the component design settles on) and the purchase is still created.
- [x] 5.1 Write failing tests for "A reconciliation mismatch is reported" and "A missing date is reported": a rejected `recordPurchase` call (mapped from the backend's `LedgerError`) is shown in place, with the entered lines/amount/date left exactly as the user had them.
- [x] 5.2 Implement the confirm action: build the `RecordPurchaseRequest` (including `capture`) from the component's state, call `recordPurchase`, and branch on success/failure per 5.0/5.1.

## 6. Abandoning a capture

- [x] 6.0 Write a failing test for "Leaving without confirming changes nothing": unmounting the capture screen (or navigating away) before confirming makes no additional API call beyond the original capture, and returning to Home shows the ledger unchanged.
- [x] 6.1 No implementation task: this scenario is satisfied by the absence of any explicit-delete call — confirm the test in 6.0 passes with no new code, or remove any code that would call one if present.
