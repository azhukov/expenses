## 1. Spike: does live scanning read the fixture receipts on the target phone

- [ ] 1.1 Build a throwaway page, not committed to `src/`, that runs `qr-scanner` on the rear camera and logs the payload. Point the phone at the three receipts behind `BE/tests/Expenses.Integration.Tests/Fixtures/1000023157.jpg`, `1000023218.jpg` and `1000023219.jpg`, using the paper if it is still at hand, otherwise the images shown full-screen on another display. Record hit or miss per receipt and per device in `BE/Expenses.Infrastructure/Extraction/FISCAL-QR.md` beside the server table. Investigation and documentation, no test. If both Aroma receipts miss on iOS, stop and revisit the engine choice (design.md, Risks) before section 7.

## 2. Domain: fiscal identity belongs to the purchase

- [x] 2.0 In `BE/tests/Expenses.Domain.Tests/`, write failing cases for the receipt-ingestion scenarios "A purchase with a fiscal identity and no image", "A purchase with both", "Deleting the image keeps the fiscal identity" and "No state without a receipt". Add a case for the D35 invariant: a purchase has an extraction state exactly when it carries an image or a fiscal identity, and setting one without the other is refused. Move the existing fiscal cases in `ReceiptFiscalPayloadTests.cs` to the new owner rather than duplicating them.
- [x] 2.1 Introduce `FiscalIdentity` as an owned value on `Purchase`, holding the supplied/extracted IKOF and JIKR, the extracted source and the payload with its source. Move the extraction state and failure reason from `Receipt` to `Purchase`, and reduce `Receipt` to the image (D35). `DetachReceipt` leaves the fiscal identity and state in place. 2.0 goes green.
- [x] 2.2 Update the application and infrastructure code that reads fiscal fields or state through `Receipt` until the solution builds. The existing tests are the guard here: this step changes where values live, not what they are, and no test is rewritten to pass.

## 3. Persistence: map the new shape onto the existing columns

- [x] 3.0 In `BE/tests/Expenses.Integration.Tests/Persistence/`, write a failing migration test for "Existing receipts keep their fiscal identity": insert a row in the pre-migration shape with a storage key, fiscal columns and a payload, apply the migration, then read it back as a purchase carrying both an image and a fiscal identity with identical values. Add a round-trip case for a purchase with a fiscal identity and no storage key.
- [x] 3.1 Remap the EF configuration: two owned types on `Purchases`, fiscal columns keeping their names, and extraction state no longer conditional on a storage key (D36). Add a non-unique index on the IKOF columns for D39.
- [x] 3.2 Generate the migration and check the generated SQL drops no column. Hand-add the one `UPDATE` clearing the fiscal source columns where no invoice was recorded (D36). The migration itself is generated code and needs no test of its own; 3.0 covers it and goes green here.

## 4. Application: fiscal-only capture, confirmation and re-run

- [x] 4.0 In `BE/tests/Expenses.Application.Tests/Receipts/`, write failing cases using the existing fakes:
  - "A payload that yields an invoice": `CaptureFiscal` returns lines, the vision fake is never called, and `TempKey` is null.
  - "The service returns no invoice": the outcome is Failed with a reason, the identifiers are reported, and vision is never called.
  - "A payload no identifier can be read from": the outcome is Failed with no identifiers, and the retrieval fake is never called.
  - "Nothing is held after a fiscal capture": no temporary store write.
  - "A confirmed fiscal capture becomes a whole purchase", "The date defaults from the payload" and "A confirmation naming neither an image nor a payload".
  - A fiscal-only confirmation re-parses the resubmitted payload rather than using echoed identifiers (D38).
  - "Re-run with no image" in `RerunExtractionTests.cs`: only the fiscal step runs.
- [x] 4.1 Make `ExtractionStepRequest.Image` nullable and build a fiscal-only cascade from the steps that do not require an image (D37). `FiscalStep` returns null when it has neither an image nor a payload.
- [x] 4.2 Add `ReceiptService.CaptureFiscal(payload)`. Make `CaptureResult.TempKey` and `CapturedReceiptCommand.TempKey` nullable, and branch confirmation on key versus payload, with a new `ApplicationErrors` code for neither (D38). Route re-run for an image-less purchase through the fiscal-only cascade. 4.0 goes green.

## 5. Application: duplicate invoice warning

- [x] 5.0 Write failing cases for "A receipt scanned twice", "A duplicate can still be confirmed", "A new invoice" and "No invoice identification code". Cover both capture paths, with the IKOF coming from a supplied payload, a decoded one and a retrieved invoice. When two purchases carry the code, the most recent is reported.
- [x] 5.1 Add the ledger lookup by IKOF and `CaptureResult.AlreadyRecorded { PurchaseId, OccurredAt }`, filled after the cascade in both capture methods (D39). 5.0 goes green.

## 6. API and MCP surfaces

- [x] 6.0 In `BE/tests/Expenses.Integration.Tests/Api/HttpAdapterTests.cs` and `Mcp/McpAdapterTests.cs`, write failing tests for:
  - "Capturing a payload", "An oversized payload", "A missing payload" and "An unparseable payload" against `POST /receipts/capture-fiscal`, using `FiscalPortalStub` for the invoice.
  - "Fiscal capture over HTTP".
  - "Reading a duplicate warning" and "No duplicate", on both capture endpoints.
  - "A purchase with a fiscal identity and no image" and "Requesting the image of an image-less purchase".
  - "MCP confirms a fiscal-only capture" and "Confirming a fiscal-only capture over either interface", with the same purchase produced by both.
  - The neither-key-nor-payload rejection, reported identically over both interfaces.
- [x] 6.1 Add the `capture-fiscal` action to `CapturesController`, with a JSON body and the same 2048-character bound (D37).
- [x] 6.2 Make the key optional in `RecordPurchaseRequest`'s capture member and in `CapturedReceiptArgument`, and describe the key-or-payload rule in the MCP tool description.
- [x] 6.3 Reshape `PurchaseView` (`hasReceiptImage`, `fiscal`) and the extraction read as D41 describes. 6.0 goes green.
- [x] 6.4 Record the new scenarios in `BE/tests/SCENARIO-COVERAGE.md`, update the capture section of `FISCAL-QR.md`, and run the `be-editorconfig-fix` checks until the BE build is clean. Documentation and tooling, no test.

## 7. FE: API client

- [x] 7.0 In `FE/src/api/writes.test.ts`, write failing cases: `captureFiscal(payload)` posts JSON to `/receipts/capture-fiscal` and returns the result; a null `tempKey` in the response is accepted; `recordPurchase` sends a capture with a payload and no key. Add the new read shape to the existing reads tests.
- [x] 7.1 Add `captureFiscal` to `writes.ts`, and update `types.ts` for the nullable `tempKey`, `alreadyRecorded`, `hasReceiptImage` and `fiscal`. 7.0 goes green.
- [x] 7.2 Add `qr-scanner` to `FE/package.json`. A package reference, no test.

## 8. FE: scan first, photograph as the fallback

- [x] 8.0 In `FE/src/routes/Capture.test.tsx`, with the scanner module replaced by a controllable fake (D40), write failing cases for:
  - "A code is read": the payload is posted with no tap, the "fetching" status is shown, and the scanner is stopped.
  - "The user chooses a photograph": the photograph control is a real `<input type="file" capture="environment">` inside a label, activated directly.
  - "Live camera refused or unavailable": the fake rejects with `NotAllowedError`, and separately reports no camera. Each shows the photograph control and no alert.
  - "Scanning is abandoned": unmounting stops the scanner, and nothing is posted.
  - "The invoice is fetched": review is shown and there is no photograph control.
  - "The invoice is not fetched": Failed with identifiers leads to the photograph, uploaded with `fiscalQr`.
  - "The code was not a fiscal code": Failed with no identifiers leads to the photograph, uploaded without `fiscalQr`.
  - "The fiscal capture cannot be reached": retry resends the same payload, and the photograph control is offered.
  - "Upload starts without a further tap" and "A payload read earlier travels with the photograph".
  - A StrictMode double mount starts one scanner and posts one payload.
- [x] 8.1 Add the scanner module wrapping `qr-scanner` behind `start(video, onRead) → stop`, with the camera-unavailable and permission-refused cases folded into one result (D40).
- [x] 8.2 Rework `Capture.tsx` into the scan → fiscal capture → (review | photograph → image capture → review) flow. Move the file-input markup and its D3 comment from `CaptureControl` onto the capture screen, add the delayed hint to try the photograph, and release the camera on the first read and on unmount. 8.0 goes green.
- [x] 8.3 In `FE/src/capture/CaptureControl.test.tsx`, first rewrite the cases for "Camera opens on activation" and "Home does not submit the image" so they fail against the current file input. Then turn `CaptureControl` into a link to `/capture` and watch them pass.

## 9. FE: review and home

- [x] 9.0 In `FE/src/routes/CaptureReview.test.tsx` and `FE/src/capture/review.test.ts`, write failing cases for:
  - "A duplicate is flagged" and "No duplicate".
  - "Confirming a fiscal-only capture": `confirmationOf` sends the payload and no key.
  - In `Home.test.tsx`, "A purchase carrying a receipt is distinguishable" for a fiscal-only purchase.
- [x] 9.1 Render the duplicate warning with the earlier purchase's date, make `confirmationOf` carry the key or the payload, and base the recent-list marker on `hasReceiptImage || fiscal !== null`. 9.0 goes green.

## 10. End to end and checks

- [ ] 10.0 Extend `FE/e2e/capture.spec.ts`. Playwright's fake camera cannot show a QR, so for this run the scanner module is replaced by one that emits a fixture payload. Cover a fiscal capture that fetches an invoice and confirms with no photograph, and one that falls back to a photograph and confirms. Run `./test-e2e.sh`.
- [x] 10.1 Run `./test-be.sh` and `./test-fe.sh`, and the `fe-style-fix` checks until `npm run style` is clean, including the `structure` rules for the new scanner module. Report which suites ran and what they returned. Tooling, no test.
- [ ] 10.2 On a phone over HTTPS, capture one receipt through each path: QR fetched, QR read but not fetched, and camera refused. Confirm each one reaches the ledger as specified. Manual verification, no test.
