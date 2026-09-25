## Context

See proposal.md for why. The pieces this builds on already exist:

- `POST /receipts/capture` accepts a `fiscalQr` form field, and `FiscalStep` prefers a supplied
  payload outright (D31). `captureReceipt(file, fiscalQr?)` in `FE/src/api/writes.ts` sends it, but
  `Capture.tsx` never passes one.
- `ExtractionCascade` runs `FiscalStep` then `VisionStep`; a step that produces lines ends the run,
  and a step that produces none hands its fiscal identity forward (D28, D29).
- `Receipt` is a value inside the `Purchase` aggregate (D2, D11). It holds the file reference, the
  extraction state and every fiscal field, so a fiscal identity cannot exist without an image.
- `CaptureResult.TempKey` and `CapturedReceiptCommand.TempKey` are non-nullable `Guid`s, and
  confirmation — `POST /purchases` with a `capture` member, and the MCP confirm tool — promotes the
  temporary image the key names.
- The capture control is a plain `<input type="file" capture="environment">`, because iOS Safari
  opens the photograph camera only for the gesture that asked for it (D3).

## Goals / Non-Goals

**Goals:**

- The free, exact path (live QR → verification service) is the normal path; the photograph and
  vision engine are reached only when it produces no invoice.
- A purchase whose receipt is an e-invoice is a first-class purchase, with no image and no
  placeholder standing in for one.
- Existing receipts, stored files and the image-capture endpoint behave exactly as before.

**Non-Goals:**

- Decoding a still photograph on the client. It reads the same frame the server's zxing-cpp reads,
  mostly with a weaker engine; the gain is the live camera, not the client.
- Fiscal capture over MCP. An assistant has no camera; it can confirm a fiscal capture, which is
  enough.
- Preventing duplicate confirmation. It is a warning (see the specs).

## Decisions

### D35 — Fiscal identity moves from `Receipt` onto `Purchase`; `Receipt` becomes the image

`Purchase` gains `FiscalInvoice? Fiscal`, an owned value holding what `Receipt` used to hold for
fiscal purposes: supplied and extracted IKOF and JIKR, the extracted source, the payload and its
source. It is named `FiscalInvoice` because Application already has a `FiscalIdentity` (the payload
parser). The `FiscalSource` and `FiscalCorroboration` enums move into it. `Receipt` keeps the storage
key, content type and size. The extraction state becomes `Purchase.ExtractionState`, held in
`Purchase.Extraction` with `Purchase.ExtractionFailureReason`. Both are present exactly when the
purchase carries an image or a fiscal invoice. `Purchase.Record` enforces that invariant, and it
drops an empty invoice so one is never kept.

A re-run can decode a code from an image that had none recorded, so `Purchase.AttachFiscalInvoice`
lets a purchase that already has an extraction state gain an invoice once. A manual purchase cannot.
Deleting the image keeps the invoice and the state. Deleting the only thing that was read clears
the state.

*Alternative: keep `Receipt` and make its file fields nullable.* Rejected with the user during
exploration. It breaks "a purchase carries a whole receipt or none", and every file access would
grow a null check for a case that is not a receipt image at all.

*Alternative: a new "evidence" wrapper holding both.* Rejected: it would be a type that exists only
to hold two optional members the aggregate can hold itself.

`FiscalInvoice` is a genuine multi-field value, not a wrapper around one primitive, so it does not
conflict with the preference for primitives.

### D36 — Persistence: same table, columns kept, one column pair cleared

Both owned types map onto `purchases`, and `Extraction` maps directly on `Purchase`. Every column
keeps its name, including `receipt_state` and `receipt_failure_reason`, and none is added or dropped.

EF reads an optional owned value as absent only when all its columns are null. The old mapping wrote
`fiscal_extracted_source` and `fiscal_payload_source` as `0` for every image, whether or not it had a
code. So the migration runs one `UPDATE` that sets both to NULL where no fiscal value was ever
recorded. Without it, every image with no code would come back with an empty invoice. The migration
test fails without that `UPDATE`; this was checked by removing it.

The check constraints are rewritten:
- The image columns are all null or all set.
- The two fiscal source columns are null together.
- `receipt_state` is present exactly when an image or an invoice is.

All constraint SQL is on one line. `core.autocrlf` is on and there is no `.gitattributes`, so a
multi-line literal would give different SQL on Windows and Linux checkouts, and the snapshot would
disagree with the model on one of them.

Rollback is the previous image plus the down migration. The down migration restores the zeros. A
fiscal-only purchase has no storage key, which the old constraint forbids, so those rows must be
dealt with before rolling back. That is noted, not engineered for.

### D37 — `POST /receipts/capture-fiscal`, a JSON body, a cascade without the vision step

A separate action on `CapturesController` taking `{ "payload": "…" }`. Same 2048-character bound,
same parse at the edge through `FiscalIdentity.From`. `ReceiptService.CaptureFiscal` runs the one
registered `ExtractionCascade` with no image. `IExtractionStep` gains `ReadsImage`, and when there
is no image the cascade skips every step that reads one and does not record it as having run.
`VisionStep` reads the image and `FiscalStep` does not. Skipping steps in the one cascade was chosen
over a second, keyed registration: it has the same effect with one less piece of composition, and
it cannot drift from the full cascade's order. It is not a special case inside `FiscalStep`.

`ExtractionStepRequest.Image` becomes nullable, and the request carries `PurchaseId` itself, since
that used to be read off the image. `FiscalStep` already never decodes when a payload is supplied;
with no image and no payload it returns null. A re-run of a purchase with no image goes down the
same path.

A consequence found while building this: `FiscalIdentity.From` used to take a whole URL with none of
the verification parameters as a bare IKOF. Under QR-first capture that would send a menu or
loyalty-card QR to the portal as an invoice code. Worse, carried into the photograph's upload, it
would stop the server decoding the real code (D31). Such a URL now yields no identifiers. A payload
that is not a URL is still kept as a bare IKOF, as before.

*Alternative: make `file` optional on the multipart endpoint.* Rejected with the user: the two
captures differ in pipeline, storage and confirmation key, and one endpoint would blur all three.

### D38 — A capture is identified by a key *or* a payload

`CaptureResult.TempKey` becomes `Guid?` — null for a fiscal capture. `CapturedReceiptCommand.TempKey`
becomes `Guid?`. The confirm path branches once, at the top: with a key it promotes the temporary
image as today; without one it requires `FiscalPayload` and creates the purchase with a fiscal
identity and no receipt. Supplying neither is rejected with a new `ApplicationErrors` code. The same command feeds HTTP and MCP, so both interfaces get the same behaviour from
one change.

A fiscal-only confirmation re-parses the resubmitted payload rather than trusting identifiers the
client echoes. That matches D30, which says one parser reads the format, and D12, which says the
server holds nothing between capture and confirm. The JIKR and fiscal source are resubmitted as for
an image capture, because only the portal answer knew them.

### D39 — Duplicate lookup by IKOF, at capture, on the established identity

After the cascade, `ReceiptService` looks up the most recent purchase whose fiscal identity carries
the IKOF the run established — supplied, decoded or retrieved — and returns it as
`CaptureResult.AlreadyRecorded { purchaseId, occurredAt }`, or null. There is one indexed lookup on
the extracted-IKOF and supplied-IKOF columns. It is not unique: two purchases may legitimately
share an invoice after the user confirms through the warning.

It runs for both capture endpoints. Running it in the cascade was rejected, because the cascade
reads receipts and does not query the ledger.

### D40 — FE: a scan screen with `qr-scanner`, the file input kept as the fallback

Flow and ownership:

```
 Home ── CaptureControl (now a link) ──▶ /capture
                                          │
                                   ┌──────┴───────┐
                                   │ Scanner      │ qr-scanner on getUserMedia,
                                   │  <video>     │ facingMode "environment"
                                   │  [Take a     │ ◀─ real <label><input capture>,
                                   │   photo]     │    same markup CaptureControl has today
                                   └──┬────────┬──┘
                           payload    │        │ File
                                      ▼        ▼
                         captureFiscal(p)   captureReceipt(file, carried?)
                                      │        ▲
                    Failed w/ ids ────┼────────┘ (carry p)
                    Failed no ids ────┼────────┘ (carry nothing)
                    Extracted/Review  ▼
                                   CaptureReview
```

- **The photograph control stays a real file input.** It is the exact markup `CaptureControl` has
  now, moved to the capture screen, so D3 still holds: the tap on it is the gesture that opens the
  camera. Home's capture action becomes a plain link. `getUserMedia` is not bound to a user gesture
  the way the file input is; the permission prompt is the browser's own.
- **`qr-scanner`** runs on the live `<video>`. It uses the native `BarcodeDetector` where one exists
  (Android Chrome) and its own worker elsewhere (iOS Safari). The scanner is reached through a small
  interface (`start(video, onRead) → stop`) that `Capture` receives from a module the tests replace.
  jsdom has neither a camera nor a worker, so unit tests drive the interface. The real library is
  exercised only in the browser.
- **The scan region is outlined on the viewfinder.** `qr-scanner` reads only a centred square of
  each frame, about two thirds of its shorter side, so a code held off-centre or too close is never
  read, and without a mark the user cannot tell. The library's own outline (`highlightScanRegion`)
  shows that square. It places the outline beside the `<video>` by the video's offset, so the video
  sits in a positioned frame of its own. No outline is drawn around a detected code: the screen
  leaves on the first read, so it would only flash.
- **Unavailable versus refused are one case.** `QrScanner.hasCamera()` false, a `NotAllowedError`
  and a missing `mediaDevices` all lead to the same state: the photograph control on its own, with no
  error (spec: *Live camera refused or unavailable*).
- **After about 10 s with no read**, the screen adds a hint to try the photograph. There is no hard
  timeout, and the photograph control is visible from the start. The 10 s figure is a constant, not
  a spec value.
- **Payload carried into the photograph** only when the failed fiscal capture reported identifiers
  (spec: *The code was not a fiscal code*). Otherwise a stray QR, such as a menu link or a loyalty
  code, would be sent as `fiscalQr`. D31 would then stop the server decoding the real code on the
  photograph.
- **The camera is released** on unmount and on the first read, by stopping every track. This is
  also how StrictMode's double effect is handled: the effect's cleanup stops the scanner it started.
  That is a different guard from the upload ref `Capture` already has, and both are needed.
  Destroying the scanner hides its outline but leaves it in the document, so releasing the scan
  removes it too.

### D41 — Read shapes: `hasReceipt` splits in two

`PurchaseView` reports `hasReceiptImage` and `fiscal` (identity or null) instead of `hasReceipt`.
The extraction view keeps its shape and is returned whenever either is present. The recent list shows
the same receipt marker for both, because the spec only distinguishes "carries a receipt" from
"carries none". A separate e-invoice glyph would be a design choice, and it is left to
implementation.

## Risks / Trade-offs

- **iOS reads with the worker engine, not a native detector** → Dense Aroma-style codes may still
  fail live. The fallback is unchanged from today, so the worst case is today's behaviour plus a
  scanning step. The first task is a spike on the user's own phone against the three fixture
  receipts, which settles this before building the rest.
- **A scanning step in front of every capture** → A receipt with no code at all costs a tap on "take
  a photo". That's accepted: fiscal receipts are the common case in the target country.
- **A fiscal-only purchase has no image to show later** → Accepted by the user during exploration.
  The payload is the verification link, so the invoice stays retrievable from the tax portal.
- **Rollback after fiscal-only purchases exist** (D36) → Documented. Those rows must be deleted or
  given a placeholder before the down migration runs.
- **`qr-scanner` is lightly maintained** (last release 2022) → It sits behind the D40 interface, so
  swapping it for `zxing-wasm` or the `barcode-detector` polyfill touches one module.
- **The breaking read shape** (`hasReceipt`, nullable `tempKey`) → The FE is the only HTTP consumer
  and changes in the same release. MCP tools describe their own shapes.

## Migration Plan

1. BE and FE ship together, as they already do through docker-compose and Railway. The read shape
   changes (D41), so no compatibility period is kept for the old FE.
2. The migration runs on startup, as every migration does today. It changes only the two fiscal source columns (D36).
3. Rollback: revert both. If fiscal-only purchases were created, deal with them first (D36).

## Open Questions

- The exact wording of the "couldn't fetch the invoice" and duplicate warnings. This is UX copy and
  can be settled during implementation.
