Test-first throughout, per `openspec/config.yaml`. Each section starts at `X.0` with the failing
tests for that section, named after the spec scenario they come from. Sections that produce no
behaviour say so.

## 1. Persist the fiscal QR payload

- [x] 1.0 Write failing tests: "A supplied payload is retained", "A decoded payload is retained",
      "An unparseable payload is still retained", "A receipt with no payload" (receipt-ingestion)
- [x] 1.1 Add a nullable fiscal QR payload to `Receipt`, recorded alongside the existing fiscal
      identifiers
- [x] 1.2 Add the column to `PurchaseConfiguration` — receipt fields live on `Purchases` (D11)
- [x] 1.3 Generate the EF migration (no behaviour of its own; exempt from the test-first rule)
- [x] 1.4 Store the payload whether or not it parsed (D32)

## 2. One parser at the boundary

- [x] 2.0 Write failing tests: "A supplied payload yields the same identifiers as a decoded one",
      "The payload is not fetched", "Identity and total are read from the code alone"
      (receipt-ingestion)
- [x] 2.1 Move `FiscalIdentity` out of `Expenses.Infrastructure.Extraction` and make it reachable
      from the API layer, leaving its parsing behaviour unchanged (D30)
- [x] 2.2 Reduce `FiscalCodeDecoder` to image bytes → payload string, so parsing has exactly one home
- [x] 2.3 Add a regression test that the payload is parsed and never dereferenced, so the
      never-fetch property fails loudly if a later edit breaks it

## 3. The capture endpoint accepts a raw payload

- [x] 3.0 Write failing tests: "Capture carrying a fiscal QR payload", "Capture carrying an
      unparseable payload", "Capture carrying an oversized payload" (api-surface)
- [x] 3.1 Accept `fiscalQr` on `POST /captures` and parse it at the edge into `FiscalIdentifiers`
- [x] 3.2 Bound the accepted payload length and reject an oversized one before parsing (D30)
- [x] 3.3 Keep `fiscalIkof`/`fiscalJikr` accepted for now, so FE and BE are never broken at the same
      time (removed in section 8)
- [x] 3.4 Carry the payload through `CapturedReceiptRequest` so confirmation can persist it

## 4. The uniform step

- [x] 4.0 Write failing tests: "Step provenance is recorded", "A step that produces lines ends the
      run", "A step that produces nothing advances the run" (receipt-ingestion)
- [x] 4.1 Define the step request as image, content type and optional fiscal identity, and the step
      result as one nullable type — collapsing `ExtractionStageOutcome`, `ExtractionResult` and
      `RetrievedInvoice` (D33)
- [x] 4.2 Reshape `IExtractionStage` to the uniform signature and delete `ExtractionStageRole`
- [x] 4.3 Delete `ExtractionStageRequest.Known`

## 5. The pipeline

- [x] 5.0 Write failing tests: "A result that does not reconcile does not advance the run", "Fiscal
      identity outlives the step that established it", "No step produces anything", "A failed
      decode falls through to the probabilistic step" (receipt-ingestion)
- [x] 5.1 Rewrite `ExtractionCascade` to run steps in registration order, advancing only where a
      step produced no candidate lines (D28)
- [x] 5.2 Replace `FiscalTrail` with field-by-field merge, first writer wins, threaded forward on
      failure (D29)
- [x] 5.3 Delete `Better()` and the best-of-failures comparison
- [x] 5.4 Remove the `ArithmeticValidator` call from the pipeline entirely

## 6. Validation becomes a label

- [x] 6.0 Write failing tests: "Validation runs once for a result" (receipt-ingestion), plus the
      existing arithmetic scenarios re-pointed at their new caller
- [x] 6.1 Call `ArithmeticValidator` once in `ReceiptService`, after the pipeline returns
- [x] 6.2 Decide the state there: no result → Failed with a reason; result + all checks pass + no
      low-confidence value → Extracted; otherwise NeedsReview
- [x] 6.3 Confirm the three checks and the two-decimal comparison (D25) are untouched — only their
      caller moved

## 7. The two steps

- [x] 7.0 Write failing tests: "Decoding is skipped when a payload was supplied", "A re-run does not
      decode the image again", "A retrieved invoice that does not reconcile is still authoritative",
      "The vision step is told what the code established" (receipt-ingestion)
- [x] 7.1 Fold decoding into the fiscal step: use the supplied or stored payload, and decode the
      image only where none is sufficient to call the portal (D31)
- [x] 7.2 Have the fiscal step carry its identity forward when the portal produces nothing
- [x] 7.3 Reduce vision to one step, last, receiving the fiscal identity as context (D23)
- [x] 7.4 Delete the second vision tier and its registration
- [x] 7.5 Point `RerunExtraction` at the stored payload rather than the image (D32)

## 8. Removals and the client

- [x] 8.0 Write failing tests: the browser-client review-reason scenarios, minus the disagreement one
- [x] 8.1 Send `fiscalQr` from `writes.ts`; stop sending `fiscalIkof`/`fiscalJikr`
- [x] 8.2 Remove the disagreeing-identifiers review reason from the capture/review screens and from
      `types.ts` (D31)
- [x] 8.3 Remove `fiscalIkof`/`fiscalJikr` from the capture endpoint and shrink
      `CapturedReceiptRequest` to payload, JIKR and source (D32)
- [x] 8.4 Delete the supplied-versus-extracted disagreement path in the application layer

## 9. Delete what the specs no longer say

- [x] 9.1 Delete the tests derived from "The expensive stage runs only on demand", "The expensive
      stage runs after a failed validation" and "Fallback also fails" — deletions, not rewrites, so
      that no test survives asserting behaviour the specs removed
- [x] 9.2 Delete the tests for supplied-versus-extracted disagreement
- [x] 9.3 Run the full suite and report what ran and what it returned

## 10. Record what changed

- [x] 10.1 Update `BE/Expenses.Infrastructure/Extraction/FISCAL-QR.md` with the payload's new route
      in and its persistence; leave the measurements as they stand (documentation; exempt)
- [x] 10.2 Note in `BE/ARCHITECTURE.md` that extraction is two steps and that validation labels
      rather than routes (documentation; exempt)
