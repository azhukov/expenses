## Why

Every capture today starts with a full photograph, and the server's own decoder reads the fiscal QR
off a stored still in only one receipt in three — so most receipts that carry a perfectly good
e-invoice fall through to the paid, probabilistic vision engine. A phone held close to the code, with
autofocus and a fresh attempt on every frame, is the better reader (D31 already says so), yet the
client never reads the code at all: `captureReceipt` accepts a payload and nothing supplies one.

Reading the QR live, and taking a photograph only when that does not produce an invoice, makes the
exact, free path the normal one and the vision engine the exception it was meant to be.

## What Changes

- **Capture becomes QR-first.** Activating capture opens a live rear-camera viewfinder that scans
  for the fiscal QR. A read payload is sent on its own, with no image, to a new fiscal-capture
  endpoint that asks the verification service for the invoice.
- **The photograph becomes the fallback.** Where no code can be read — the user skips, the camera is
  unavailable or refused, or the device has none — or where a code was read but the verification
  service did not return an invoice, the client falls back to today's full-photograph capture. In the
  second case the payload it already read travels with the photograph.
- **A purchase can carry a fiscal identity without an image.** Fiscal identity (the payload, invoice
  and issuer verification codes, and how each was established) is split off the receipt image and
  belongs to the purchase directly. A purchase carries a fiscal identity, an image, both, or neither.
  Extraction state is recorded wherever the purchase carries either. **BREAKING** for the purchase
  and extraction read shapes, which move fiscal fields out from under the image.
- **New endpoint `POST /receipts/capture-fiscal`**, taking a payload and no file. It runs the fiscal
  step only — never the vision engine, having nothing to show it — and returns a capture with no
  temporary key. Confirmation of such a capture goes through the existing purchase-recording path,
  carrying the payload instead of a key.
- **Re-running extraction for a purchase with no image** runs the fiscal step alone.
- **Duplicate invoices are warned about at review.** Where a capture's invoice identification code
  is already recorded on a purchase, the capture response names that purchase and the review screen
  says so. Confirming is still allowed.
- The multipart `POST /receipts/capture` is unchanged and keeps accepting `fiscalQr`.

## Capabilities

### New Capabilities

_None._ Every behaviour here extends an existing capability.

### Modified Capabilities

- `receipt-ingestion`: fiscal identity separated from the receipt image; a capture with a payload
  and no image; extraction state for an image-less purchase; re-run without an image; duplicate
  invoice detection at capture.
- `api-surface`: the fiscal-capture endpoint; confirming a capture with no temporary key over both
  interfaces; read shapes reporting fiscal identity apart from the image.
- `browser-client`: QR-first capture with a live viewfinder, the photograph as fallback, the payload
  carried into the fallback, the duplicate warning at review, and the recent-purchases marker for a
  purchase whose receipt is an e-invoice rather than a photograph.

## Impact

- **FE:** a new dependency, `qr-scanner`; a scan screen ahead of the existing file-input capture;
  `Capture` and `CaptureReview` learn an image-less capture; `api/writes.ts` and `api/types.ts`
  follow the new shapes. HTTPS is already required for the camera and already available through
  `plugin-basic-ssl`.
- **BE Domain:** `Purchase` gains a fiscal identity of its own; `Receipt` becomes the image alone;
  extraction state moves to where it covers either.
- **BE Application/Infrastructure:** a fiscal-only capture on `ReceiptService`, a cascade run that
  holds only the fiscal step, a lookup of purchases by invoice identification code, and an EF
  migration that moves the fiscal columns without losing data.
- **BE API/MCP:** the new controller action; the capture argument to `POST /purchases` and the MCP
  confirm tool accepting a capture with no key; view models reshaped.
- **Specs:** `receipt-ingestion`, `api-surface`, `browser-client`.
- **Out of scope:** capturing a fiscal payload over MCP (an assistant has no camera; confirming is
  enough); client-side QR decoding of a still photograph; blocking duplicate confirmation.
