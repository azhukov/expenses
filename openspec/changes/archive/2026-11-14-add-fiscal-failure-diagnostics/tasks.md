# Tasks

## 1. Diagnosis types and signatures (exempt from test-first: signatures only, with behaviour covered in groups 2–4)

- [x] 1.1 Add `FiscalDiagnosisKind` and `FiscalDiagnosis` to `Expenses.Application/Dtos/Extraction` (design D42).
  - Include a static helper that truncates a response excerpt to 200 characters.
  - Verify with `dotnet build BE/Expenses.sln`.
- [x] 1.2 Add the `FiscalRetrieval(ExtractionStepResult? Invoice, FiscalDiagnosis Diagnosis)` record and change `IFiscalInvoiceRetrieval.Retrieve` to return it (design D43).
  - Add `FiscalDiagnosis? Diagnosis` to `ExtractionStepResult` (settable through `From`/`FiscalOnly` and a `WithDiagnosis`).
  - Add it to `CascadeOutcome`, and as a trailing optional `FiscalDiagnosis` to `CaptureResult`.
  - Adapt the callers mechanically, so that `FiscalPortalClient` wraps its existing `null`s with a placeholder kind for now.
  - Verify with `dotnet build BE/Expenses.sln` and that `./test-be.sh` stays green.

## 2. Portal client classification

- [x] 2.0 Rewrite the `null` assertions in `Expenses.Integration.Tests/Extraction/FiscalPortalTests` as kind assertions, and add the missing cases, using `FiscalPortalStub` (extend the stub to answer a chosen status, a blank body, malformed JSON, JSON with no items, and to stall past the timeout). Run them to see each one fail:
  - Retrieved: a fresh fetch, and a cache hit.
  - `PortalRejected` with status 500 and with status 404.
  - `NoRecord` for a blank 200.
  - `UnparseableResponse` for malformed JSON, with the excerpt at most 200 characters.
  - `UnparseableResponse` for an invoice with no items.
  - `PortalUnreachable` for a timeout, and for a refused connection (an unused port).
  - `IncompleteIdentifiers` with `MissingParameters` naming exactly the absent ones of `iic`/`tin`/`crtd`.
  - A failure is not cached, so a second call asks the stub again.
- [x] 2.1 Implement the classification in `FiscalPortalClient` per the D43 table.
  - Split the combined `catch`, and tell a timeout apart from the caller's cancellation (which still propagates).
  - Log non-`Retrieved` outcomes at Warning (D46).
  - Verify that every 2.0 test passes, along with `FiscalJourneyTests` and `FiscalCaptureTests`.

## 3. Fiscal step and cascade

- [x] 3.0 Write failing tests and run them to see each one fail.
  - In `Expenses.Application.Tests/Extraction/ExtractionCascadeTests`, using `FakeStep` extended to emit a diagnosis:
    - The first diagnosis reaches `CascadeOutcome.Diagnosis` both when the run ends in lines and when it fails.
    - A lineless result with a diagnosis leaves `StepsRun`, the merged identifiers and the outcome unchanged compared with returning `null` (spec "Diagnosis does not change the outcome").
  - In an integration-level `FiscalStep` test (beside `ExtractionPipelineTests`):
    - `NoPayload` when there is no payload and no decodable image, including a decoder that throws.
    - `NotFiscal` for a non-fiscal URL, with the portal stub receiving no request.
    - The retrieval's diagnosis passed through.
    - `UnparseableResponse` when retrieval throws.
- [x] 3.1 Implement D44 in `FiscalStep` (never return `null`, always carry a diagnosis) and D45 in `ExtractionCascade`. Verify that every 3.0 test passes and the full `./test-be.sh` is green.

## 4. Capture responses

- [x] 4.0 Write failing tests, and run them to see each one fail.
  - In `Expenses.Application.Tests/Receipts/CaptureFiscalTests` and `CaptureReceiptTests`: `CaptureResult.FiscalDiagnosis` is copied from the outcome for both use cases.
  - In `Expenses.Integration.Tests/Api/FiscalCaptureTests`, over HTTP:
    - Spec "Reading why a fiscal capture fetched nothing": the stub is down, the response is a 200 with `state: "Failed"` and `fiscalDiagnosis.kind: "PortalUnreachable"`.
    - "Reading missing parameters".
    - "Reading a refusal": status 500.
    - "A retrieved invoice".
    - An image capture's response carries the field.
  - Assert the kind serialises as a string.
- [x] 4.1 Set `FiscalDiagnosis` in `ReceiptService.Capture` and `CaptureFiscal`. Verify that every 4.0 test passes. Update `BE/Expenses.Infrastructure/Extraction/FISCAL-QR.md` with the diagnosis kinds and what each one means operationally.

## 5. Capture screen

- [x] 5.0 Write failing tests first.
  - `FE/src/capture/fiscalNotice.test.ts`: one case per kind plus `null` (D47), checking the message text, the HTTP status interpolated, readable names for missing parameters, and `showPayload` only for `NotFiscal`/`IncompleteIdentifiers`.
  - `FE/src/routes/Capture.test.tsx`: the browser-client scenarios.
    - "The verification service was unreachable": the specific message, and the photograph is uploaded with the payload.
    - "The code was not a fiscal code": the message and the read text are shown, and the photograph is uploaded without the payload.
    - "The fiscal code was incomplete": the missing names and the read text.
    - A Failed response with no `fiscalDiagnosis`: the generic message.
- [x] 5.1 Add `FiscalDiagnosis`/`FiscalDiagnosisKind` to `FE/src/api/types.ts` and implement `capture/fiscalNotice.ts`.
  - In `Capture.tsx`, render the notice and, when `showPayload` is set, the payload in a wrapping `<code>` (add the style in `Capture.module.css`).
  - Verify that the 5.0 tests pass and that `./test-fe.sh` is green (lint, format, structure rules included; use the `fe-style-fix` skill if needed).
- [x] 5.2 Extend `FE/e2e/fiscal-capture.spec.ts` with the portal stub (`FE/e2e/portal/server.mjs`) answering 500. Verify that the screen shows the refusal message with the status, and that `./test-e2e.sh` passes.

## Workflow follow-up

- Check on a device: scan a non-fiscal QR, and scan a real receipt with the portal blocked. Confirm that each message reads correctly on a phone (see `FE/DEVICE-CHECKLIST.md`).
- Archive the change with `/opsx:archive` once it is implemented and reviewed.
