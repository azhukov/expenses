# The deterministic extraction path

What was measured before this was built, and what the fiscal verification service actually returns.
It is written down so the next person does not repeat the spike, and so a decoder or portal change
can be judged against numbers rather than impressions.

## The decoder comparison

Three photographed thermal receipts, committed as fixtures in
[../../tests/Expenses.Integration.Tests/Fixtures/](../../tests/Expenses.Integration.Tests/Fixtures/).
Both decoders were run at four rotations and five scales, on the full photograph and on a
hand-cropped, perfectly framed symbol — quiet zone and all three finder patterns, about 525 px
across.

| receipt | till | ZXing.Net 0.16.11 | zxing-cpp 3.1.1 (`ZXingCpp` 0.5.3) |
| --- | --- | --- | --- |
| `1000023157.jpg` | Aroma | miss | miss |
| `1000023218.jpg` | Aroma | miss | miss |
| `1000023219.jpg` | Megapromet | miss | **hit**, from the unmodified 4000x3000 photograph |

Two things follow, and both are load-bearing (D21):

- **Cropping and rectification bought nothing.** Neither decoder read a perfectly framed symbol it
  could not read in the photograph. The preprocessing ladder and its time budget were therefore
  deleted rather than tuned; more combinations cost time on every image and rescued no measured miss.
- **The wall is per-till, not universal.** The Aroma symbol is too dense for the print quality it is
  printed at; the Megapromet one decodes with no preprocessing at all. A miss is an ordinary outcome
  on some tills and must stay one everywhere in the code.

Decoding the full photograph costs roughly 150–600 ms on a development machine, and the same on
linux-x64 in the runtime image — the native binary the package ships resolves there and was run
there rather than assumed.

If the hit rate proves too low in use, the next thing to try is OpenCV's `WeChatQRCode` — a CNN
detector with a super-resolution model — not more preprocessing. It needs a native OpenCV dependency
and separately distributed model files, which is why it was not the first move.

## How a payload reaches the server

Three routes, one parser:

1. **On its own**, as `POST /receipts/capture-fiscal { "payload": "…" }`. This is the normal route
   for the browser client, which scans the code live before it asks for a photograph. Only the
   fiscal step runs, because there is no image for the vision engine. If the portal returns the
   invoice, that is the whole capture: the purchase is confirmed by its payload and carries no
   image (D37, D38).
2. **Beside a photograph**, as the `fiscalQr` form field on `POST /receipts/capture`. The client
   does this when the portal returned no invoice for a payload that did yield identifiers. It never
   does it for a code that yielded none, such as a menu link. Under D31 a supplied payload stops the
   server decoding the image, so a stray payload would hide the receipt's real code.
3. **Decoded by the server** from the stored image, when no payload was sent at all.

Every route goes through `FiscalIdentity.From`, so a client and the server cannot drift about a
format with two traps in it (D30). Both endpoints bound the payload at 2048 characters. An
unrecognised payload is an ordinary capture carrying no identifiers, not a bad request. A URL
carrying none of the verification parameters counts as unrecognised: it is some other QR, not an
invoice code (D37).

**The payload is parsed and never dereferenced.** It is a URL; it is not fetched. The verification
service is addressed from `PortalOptions`, and only the payload's parameters are read.

**It is retained on the purchase's fiscal invoice**, verbatim, whether or not anything could be
parsed out of it (D32, D35). That is for re-runs above all. Decoding a stored photograph reads one
symbol in three, so a purchase whose code a client read at capture would lose that reading on every
later extraction if only the parsed identifiers survived; and a purchase captured from its code
alone has no photograph to decode at all. It also leaves an unrecognised format recoverable — the
response shape below was observed from exactly one invoice, so learning a field later is expected,
and a discarded payload cannot be re-read.

Because the payload states the invoice code, the issuer tax number, the creation timestamp and the
total, the capture-to-confirm round trip carries three fiscal members rather than six: the payload,
the JIKR — which only the portal knows — and how the identity was established.

## What the QR carries

The payload is a verification URL, and its parameters live after the `#`, so the fragment is what
must be parsed:

```
https://mapr.tax.gov.me/ic/#/verify
  ?iic=32AA324CFF5030271E16D59F7F8EF636   invoice identification code (IKOF)
  &tin=02365928                            issuer tax number
  &crtd=2026-08-29T14:59:22+02:00          creation timestamp
  &ord=123358&bu=mr388op181&cr=ov783dz180&sw=zj126cg820
  &prc=59.65                               invoice total
```

`crtd` carries a `+` in its UTC offset. A form decoder reads `+` as a space, which is why the
parameters are split by hand: the portal would otherwise be asked about an invoice created at no
time at all.

**There is no JIKR in the code.** Before this change `crtd` was read into the JIKR slot, so every
JIKR the shipped code recorded was a timestamp. The JIKR is the portal's `fic` and is knowable no
other way (D24). Note also that it is printed hyphenated by one ERP and unhyphenated by another, so
identifiers are compared without hyphens while both readings are retained exactly as read.

## The portal contract, as observed

Undocumented, and read out of the portal's own JavaScript bundle. It may change without notice.

```
POST https://mapr.tax.gov.me/ic/api/verifyInvoice
Content-Type: application/x-www-form-urlencoded

iic=<IKOF>&dateTimeCreated=<crtd, verbatim>&tin=<issuer tax number>
```

No captcha token is required on this endpoint; the portal's reCAPTCHA is wired to a different form.
The answer is roughly 14 KB of JSON. What is mapped, and nothing else is:

| field | becomes |
| --- | --- |
| `items[].name`, `.unit`, `.quantity`, `.priceAfterVat`, `.vatRate` | a candidate line, verbatim |
| `items[].unitPriceAfterVat` × `.quantity` | the line's list price |
| `items[].rebate` | the line's discount |
| `seller.name`, `seller.idNum` | the merchant and its tax number |
| `totalPrice`, `totalVATAmount` | the result's total and tax amount |
| `sameTaxes[].vatRate` | the result's tax rate, **only** when the invoice has exactly one |
| `fic` | the JIKR |

Two details that are easy to get wrong:

- **`unitPriceAfterVat` is a unit price; the discount check is line-level.** Six of the fourteen
  lines on the recorded invoice have a quantity other than 1 — 15.00 per kilogram over 0.548 kg —
  so the list price is extended by the quantity before it reaches the check. Passing the unit price
  through verbatim would fail the arithmetic on every weighed line and send an authoritative invoice
  to review.
- **Line amounts are stated at four decimal places and the total at two.** Their raw sum is
  `59.6515` against a stated `totalPrice` of `59.65`, so both sides are rounded to two places before
  comparison — the precision the ledger records — rather than compared raw (D25).

The response recorded once from the real portal is committed as
`Fixtures/verify-32AA324CFF5030271E16D59F7F8EF636.json`. It is the only sanctioned copy, and no test
reaches the network (D26).

## What this does not answer

- **Retention is unknown.** Whether the portal still answers for an old receipt has not been tested.
  If lookups expire, the deterministic path serves only recent receipts and vision extraction is the
  permanent fallback rather than the emergency one — which is a different design, not a smaller one.
  It decides whether stage 2 is the main path or a nice case.
- **The response shape was observed on one invoice.** A refund, a corrected invoice, or a different
  till may return fields nothing here has seen. Anything unmapped is ignored rather than guessed at,
  and a shape that cannot be read is a stage that produced nothing.
