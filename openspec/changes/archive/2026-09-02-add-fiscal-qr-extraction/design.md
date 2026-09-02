## Context

The cascade, the candidate lifecycle, the arithmetic oracle and the `IExtractionStage` port all
exist and work (D12, D20). What does not exist is an engine. This change fills that gap on the
deterministic side only.

Three facts were measured on `examples/` before this was designed, and they shape every decision
below.

**The QR payload is a verification URL with a stable parameter set.** Decoded from
`1000023219.jpg`:

```
https://mapr.tax.gov.me/ic/#/verify
  ?iic=32AA324CFF5030271E16D59F7F8EF636   invoice identification code (IKOF)
  &tin=02365928                            issuer tax number
  &crtd=2026-08-29T14:59:22+02:00          creation timestamp
  &ord=123358&bu=mr388op181&cr=ov783dz180&sw=zj126cg820
  &prc=59.65                               invoice total
```

The parameters live after the `#`, so the fragment must be parsed, not the query.

**The verification service returns the whole invoice.** `POST /ic/api/verifyInvoice` with
`iic`, `dateTimeCreated` and `tin` as form fields returns ~14 KB of JSON containing `items[]`
(name, code, unit, quantity, `unitPriceAfterVat`, `rebate`, `vatRate`, `priceAfterVat`), `seller`
(name, TIN, address, town), `sameTaxes[]` (per-rate VAT breakdown), `totalPrice`,
`totalPriceWithoutVAT`, `totalVATAmount`, `invoiceNumber`, and `fic`. No captcha token is required
on this endpoint; the portal's reCAPTCHA is wired to a different form.

**Decoding is the bottleneck, and it is per-till.** Across both decoders, at four rotations and
five scales, on the full photograph and on a hand-cropped, perfectly framed symbol:

| receipt | ZXing.Net 0.16.11 | zxing-cpp |
|---|---|---|
| `1000023157` Aroma | miss | miss |
| `1000023218` Aroma | miss | miss |
| `1000023219` Megapromet | miss | hit (full photo, no preprocessing) |

## Goals / Non-Goals

**Goals:**

- Extract a complete, authoritative invoice for any receipt whose fiscal QR decodes.
- Never ask a probabilistic stage for a value a deterministic one has already established.
- Keep a decode miss an entirely ordinary outcome, because two of three sample receipts miss.
- Correct the JIKR source defect.
- Keep the test suite deterministic and offline.

**Non-Goals:**

- Implementing real vision extraction. The placeholder stays as the fallback.
- Supporting fiscalisation schemes other than Montenegro's. One portal is assumed.
- Client-side decoding at capture. It is the better long-term answer for the misses, but `FE/`
  does not exist yet.
- Improving the hit rate beyond swapping the decoder. Cropping and rectification were measured and
  bought nothing.

## Decisions

### D21 — Replace ZXing.Net with zxing-cpp

ZXing.Net read none of the three symbols, including one that a better decoder reads from the raw
photograph with no preprocessing. That is not a tuning gap; the binarizer is the limit. The
preprocessing ladder in `FiscalCodeDecoder` (three scales, a time budget) can go with it: the
measured hit needed none of it, and the two measured misses were not rescued by any of it.

Alternatives considered: OpenCV's `WeChatQRCode` (CNN detector plus a super-resolution model) is
likely better still, but it needs a native OpenCV dependency and separately distributed model
files. If the hit rate on real use proves too low, that is the next thing to try, not more
preprocessing.

### D22 — Fiscal decoding becomes the primary stage; vision becomes the fallback

The role ordering inverts. `FiscalDecodeStage` stays `Opportunistic` at the head of the cascade, a
new portal stage follows it as `Primary`, and the placeholder vision stages become `Fallback`.
`IExtractionStage` is unchanged and `ExtractionStageOutcome` already carries either a result or
fiscal identifiers. Three things around it did have to move, and they were found by building it:

- **The cascade validates after every producing stage, not after two named tiers.** With both
  placeholders sharing the `Fallback` role, "cheap first, expensive only on demand" cannot be
  expressed by role alone. Stages now run in role then registration order, each answering a check
  the one before it failed, and the first result that reconciles ends the run. That is also exactly
  what "a deterministic result ends the cascade" means, so the two requirements collapse into one
  rule rather than two.
- **`ExtractionStageRequest.Known` did not in fact carry what an earlier stage decoded** — it
  carried only what the upload supplied. It now carries both, which the portal stage needs: without
  the decoded `iic`, `tin` and `crtd` it has nothing to ask about.
- **`ExtractionStageOutcome` gained an optional fiscal source.** A stage that asked the verification
  service knows something neither a decode nor printed text can tell, and the receipt records that
  distinction (see D24). Role alone cannot express it, since the portal stage is `Primary` and so
  would otherwise be read as having read printed text.

### D23 — The QR is useful even when the portal is not

`prc` carries the total and `tin` the merchant, so a decode alone yields an exact total, merchant
and timestamp with no network call. When the portal is unreachable, that is still strictly better
than nothing: it turns the arithmetic oracle from a self-consistency check into a check against a
known-true total, which is precisely the weakness D20 recorded about itself.

### D24 — JIKR comes from the portal's `fic`, never from the QR

`FiscalIdentity.From` currently reads `crtd` into the JIKR slot. `crtd` is the creation timestamp.
The JIKR for `1000023219` is `d2857c6a-a363-4173-bf9c-dff37f77741a`, which appears in the portal
response as `fic` and appears nowhere in the QR at all. Every JIKR the shipped code has recorded is
a timestamp. The identifier is simply unknown until the portal answers, and the specs now say so.

Note the format varies by ERP: printed hyphenated on the Aroma receipts and unhyphenated on the
Megapromet one. D10's rule that no format is imposed on a fiscal identifier is what makes this
survivable — comparison should be format-insensitive.

Since the identifier is knowable only from the service, `Receipt.FiscalSource` gains
`RetrievedFromService` beside `DecodedFromCode` and `ReadAsText`. Both of the first two are exact
and neither is displaced by printed text; what separates them is that only one of them can name the
JIKR at all.

### D25 — Round to two decimal places before comparing

The portal states line amounts at four decimal places: `3.9008`, `2.4196`, `5.7716`, `3.5595`.
Their raw sum is `59.6515` against a stated `totalPrice` of `59.65`. The current sum check compares
raw values and would reject the authoritative invoice as failing arithmetic. Rounding each side to
the precision the ledger stores is not a tolerance — it is comparing at the precision the values
actually have.

### D26 — The portal is an outbound dependency, and tests never touch it

The verify response is recorded once and committed beside the images. Unit and integration tests
run against the recording; only a separately tagged spike test may reach the network. This keeps
the suite deterministic and offline, and it is the only way the config's test-first rule can be
honoured for a stage whose real behaviour lives on someone else's server.

`examples/*.jpg` move into the repository as fixtures. `1000023219.jpg` is the decoding hit,
`1000023157.jpg` and `1000023218.jpg` are the misses, and having both outcomes as committed
fixtures is what stops a future decoder change from silently regressing either.

### D27 — A retrieved invoice is not re-derived

Line amounts, quantities, units, VAT rates and the seller are taken verbatim. The `rebate` field
maps to the existing discount concept. Nothing recomputes what the tax authority already stated.

One value is derived rather than copied, and the first draft of this decision got it wrong.
`unitPriceAfterVat` is a **unit** price while the discount check is line-level — it asks whether the
list price less the discount equals the line amount — so the list price is `unitPriceAfterVat`
extended by the quantity. Six of the fourteen lines on the recorded invoice are priced by weight or
by the ten: 15.00 per kilogram over 0.548 kg is a line that lists at 8.22 and costs 8.22. Passing
the unit price through verbatim would have failed the arithmetic on every one of them and sent an
authoritative invoice to review, which is the opposite of what this change is for.

The per-line rate needs somewhere to live, so `ExtractionCandidate` gains a nullable
`TaxRatePercent`: an invoice may carry several rates — this one carries 21% and 7% — so the line's
rate cannot be inferred from the result's. For the same reason the result states a rate only where
the invoice has exactly one; the tax amount it stated stands on its own either way.

## Risks / Trade-offs

- **Two of three sample receipts do not decode.** The deterministic path is therefore a partial
  solution, and the honest hit rate is recorded rather than assumed. Mitigated by every miss
  behaving exactly as a receipt carrying no code, which the specs require and the existing cascade
  already does.
- **A government portal is now on the critical path.** Availability, rate limiting and terms of use
  are all outside our control, and the endpoint is undocumented — it was read out of the portal's
  own JavaScript bundle and may change without notice. Mitigated by treating any failure as a stage
  that produced nothing, and by never blocking ingestion on it.
- **Retention is unknown.** Whether the portal still answers for an old receipt has not been
  tested. If lookups expire, the deterministic path only serves recent receipts and vision becomes
  the permanent fallback rather than the emergency one.
- **Undocumented response shape.** `items[]` was observed on one invoice. A different till, a
  refund, or a corrected invoice may return fields this design has not seen. Mitigated by mapping
  defensively and letting anything unmapped fall to review rather than be invented.
- **Swapping decoders swaps a native dependency.** zxing-cpp ships native binaries; ZXing.Net is
  managed. This affects the container image and must be checked against the deployment target.
- **Privacy.** Receipt identifiers are sent to the tax authority that issued them. That is a small
  exposure — they already hold the invoice — but it is a real outbound flow and should be stated
  rather than assumed away.
