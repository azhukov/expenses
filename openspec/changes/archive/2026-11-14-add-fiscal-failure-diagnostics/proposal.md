# Proposal

## Why

When a scanned receipt yields no invoice, the capture screen only says "We could not fetch the
invoice for this receipt. Photograph the receipt instead." This happens often. The user can't tell
whether the camera read the wrong QR code, the fiscal portal was down, or the portal answered with
something the ledger couldn't read. So they can't tell whether to rescan, retry later, or report a
bug. The cause is known on the server and is thrown away: `FiscalPortalClient` and `FiscalStep`
collapse every failure into `null` (D26) and log it at Debug level.

## What Changes

- Every capture response reports a **fiscal diagnosis**: why the fiscal step produced no invoice, or
  that it produced one. It is an additive, nullable field and is informational only. It never turns
  a capture into an error, and it never changes the cascade, the photograph fallback, or the outcome
  state.
- The diagnosis is one of a closed set of kinds, with a short detail:
  - `NoPayload`: no QR payload was supplied, and none could be decoded from the image.
  - `NotFiscal`: a payload was read, but it carries none of the verification parameters (`iic`,
    `tin`, `crtd`), for example a menu or loyalty-card QR.
  - `IncompleteIdentifiers`: some verification parameters were found but not all three the portal
    needs. The diagnosis names the missing ones.
  - `PortalUnreachable`: there was a network failure or a timeout.
  - `PortalRejected`: the portal answered with a non-success HTTP status. The diagnosis carries the
    status code.
  - `NoRecord`: the portal answered successfully with an empty body, meaning it has no such invoice.
  - `UnparseableResponse`: the portal's body was not the expected JSON, or it had no line items. The
    diagnosis carries a short, truncated excerpt of the body.
  - `Retrieved`: the invoice was fetched. This includes reuse of an earlier answer.
- The capture screen states the specific reason in place of the generic notice. For `NotFiscal`,
  `IncompleteIdentifiers` and `NoPayload` cases it also shows the raw text the camera read, so the
  user can see which code was scanned. The photograph control and the rules for which payload
  travels with the photograph are unchanged.
- Portal failures are logged at Warning instead of Debug, so they show up in the durable log without
  turning on debug logging.

Not part of this change: retrying the portal automatically, persisting the diagnosis on the
purchase, reporting it over MCP (capture is HTTP-only), or changing which steps run.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `receipt-ingestion`: the portal-retrieval requirement no longer treats every failure as silent
  nothing. The run classifies why no invoice was obtained and reports it with the capture, while
  still continuing exactly as before.
- `api-surface`: both capture endpoints carry the fiscal diagnosis in their response.
- `browser-client`: when a fiscal capture yields no invoice, the screen states the diagnosed reason
  and, where relevant, what was read.

## Impact

- **Backend**:
  - A new `FiscalDiagnosis` DTO with a kind enum in `Expenses.Application/Dtos/Extraction`.
  - `IFiscalInvoiceRetrieval.Retrieve` returns the result together with a diagnosis.
    `FiscalPortalClient` classifies each failure branch it already has.
  - `FiscalStep` diagnoses the no-payload, not-fiscal and incomplete cases. It carries the diagnosis
    on `ExtractionStepResult`.
  - `ExtractionCascade` puts it on `CascadeOutcome`, and `ReceiptService` puts it on
    `CaptureResult`.
  - Portal log levels change.
  - The test fakes and the portal stub need diagnosis-aware cases.
- **Frontend**:
  - `api/types.ts` gains the `fiscalDiagnosis` field.
  - `Capture.tsx` maps each kind to a message, and shows the payload where relevant.
  - Labels live with the existing ones.
- **API**: additive JSON field on `POST /receipts/capture` and `POST /receipts/capture-fiscal`, so
  existing clients are unaffected.
- **Data**: no migration. Nothing is persisted.
