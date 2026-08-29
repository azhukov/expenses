## Why

Extraction today is a placeholder: `PlaceholderReceiptExtractor` derives fake lines from the image
hash and no real engine exists (D12, D20). Before reaching for a vision model, a measurement on the
three receipts in `examples/` showed that a deterministic path exists and is strictly better where
it applies.

The fiscal QR on a Montenegrin receipt is a verification URL into a single national portal
(`mapr.tax.gov.me`). Decoding it and calling that portal returns the **complete authoritative
invoice** — every line item with code, name, quantity, unit, VAT rate and amount, plus the seller,
the totals, the VAT breakdown and the FIC. It is exact, free, cannot hallucinate, and reconciles by
construction. A vision model can only ever approximate what this returns verbatim.

Measured, not assumed (all three `examples/` receipts):

| | ZXing.Net 0.16.11 (shipped) | zxing-cpp |
|---|---|---|
| `1000023157` (Aroma) | miss | miss |
| `1000023218` (Aroma) | miss | miss |
| `1000023219` (Megapromet) | miss | **hit** |

Cropping to a perfectly framed symbol — quiet zone and all three finder patterns, ~525 px across —
changed nothing for either decoder. So D20's "signal-quality wall" is real but is **per-till, not
universal**: the Aroma symbol is too dense for its own print quality, while the Megapromet symbol
decodes from the raw 4000x3000 photo with no preprocessing at all. The honest expectation is that
this path hits on some receipts and misses on others, and that a miss must stay an ordinary outcome.

## What Changes

- Replace the QR decoder with one that can actually read these symbols. The shipped ZXing.Net
  decoder scored zero across every combination tried; zxing-cpp read a symbol it could not.
- Promote fiscal decoding from an opportunistic identity stage to a **primary extractor**. Where the
  QR decodes, the QR alone yields merchant TIN, exact timestamp, IIC and — in `prc` — the receipt
  total, with no network call at all.
- Add a deterministic extraction stage that calls the fiscal verification portal with the decoded
  identifiers and maps the returned invoice onto extraction candidates. All receipts are assumed to
  come from this one portal.
- **BREAKING (defect fix)**: `FiscalIdentity.From` maps the QR's `crtd` parameter to JIKR.
  `crtd` is the invoice creation timestamp; the JIKR is not in the QR at all. It is the portal's
  `fic` field. Every JIKR recorded by the shipped code is wrong.
- Require the arithmetic validator to compare at 2 decimal places. The portal returns line amounts
  at 4 dp whose raw sum is 59.6515 against a stated total of 59.65 — authoritative data that the
  current check would reject.
- Keep vision extraction as the fallback for receipts whose QR does not decode. This change does not
  implement it; it makes room for it.

## Capabilities

### New Capabilities

None. This changes how an existing capability extracts, not what the ledger is.

### Modified Capabilities

- `receipt-ingestion`: fiscal decoding becomes a primary deterministic extractor rather than an
  opportunistic identity stage; a portal lookup stage is added; the JIKR source is corrected; the
  arithmetic check gains a stated rounding precision.

## Impact

- `Expenses.Infrastructure/Extraction/FiscalCodeDecoder.cs` — decoder swap and the `crtd`/JIKR fix.
- `Expenses.Infrastructure/Extraction/Stages.cs` — fiscal stage changes role; new portal stage.
- `Expenses.Infrastructure/ExpensesInfrastructure.cs` — cascade re-wiring and portal options.
- `Expenses.Application/Extraction/ArithmeticValidation.cs` — stated comparison precision.
- New outbound dependency on `mapr.tax.gov.me`, and a new decoder package replacing ZXing.Net.
- `examples/*.jpg` become committed test fixtures, with a recorded portal response beside them so
  tests stay deterministic and offline.
