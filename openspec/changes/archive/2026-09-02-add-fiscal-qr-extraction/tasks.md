# Tasks

Working discipline: each behaviour section starts at `X.0` with the tests for that section, and no
implementation task in a section may be started while its `X.0` is unchecked. Scenario names in
brackets refer to `specs/receipt-ingestion/spec.md`.

## 1. Fixtures and dependencies

No behaviour — no tests. These tasks only move bytes and packages into place.

- [x] 1.1 Move `examples/1000023157.jpg`, `1000023218.jpg`, `1000023219.jpg` into the test fixture
      directory and commit them. They are currently untracked.
- [x] 1.2 Record the verify response for `1000023219` and commit it beside the images as
      `verify-32AA324CFF5030271E16D59F7F8EF636.json`. This is the only sanctioned copy; tests never
      call the portal (D26).
- [x] 1.3 Add the zxing-cpp package reference and remove `ZXing.Net` from
      `Expenses.Infrastructure.csproj` (D21).
- [x] 1.4 Confirm the native binaries zxing-cpp ships resolve in the container image, not only on
      the dev machine.

## 2. Decoding

- [x] 2.0 Write the failing decoder tests. `1000023219.jpg` decodes from the unmodified full-resolution
      photograph [A decodable symbol is decoded from an unmodified photograph]; `1000023157.jpg` and
      `1000023218.jpg` return no code and throw nothing [Decoding fails]; a non-image and a PDF are
      misses rather than errors [Image carries no fiscal code at all]. The two misses are as much a
      part of the contract as the hit — assert them, do not skip them.
- [x] 2.1 Replace the decoder implementation behind `IFiscalCodeDecoder` with zxing-cpp.
- [x] 2.2 Delete the scale ladder and the time budget. The measured hit needed neither, and neither
      rescued a measured miss (D21).

## 3. Reading the decoded payload

- [x] 3.0 Write the failing payload tests against the decoded string for `1000023219`: the fragment
      is parsed, and `iic`, `tin`, `crtd` and `prc` are all read [Identity and total are read from the
      code alone]; a payload that is not a URL is retained verbatim; `crtd` is NOT read into the JIKR
      slot and the JIKR is reported as not yet known [An identifier the fiscal code does not carry].
      That last one must fail against today's code — it is the defect.
- [x] 3.1 Correct `FiscalIdentity.From` so JIKR is never populated from `crtd` (D24).
- [x] 3.2 Carry the total, the issuer tax number and the creation timestamp out of the payload
      alongside the identifiers.
- [x] 3.3 Make fiscal identifier comparison insensitive to hyphenation, since the JIKR is printed
      hyphenated by one ERP and unhyphenated by another (D24).

## 4. Retrieving the invoice

- [x] 4.0 Write the failing portal-client tests against the committed recording: the response maps to
      candidates with description, quantity, unit, VAT rate and amount, and to a merchant with name
      and tax number [Invoice detail is retrieved and mapped]; `rebate` maps to the discount and
      `unitPriceAfterVat` to the list price; `fic` becomes the JIKR [The missing identifier arrives
      from the verification service]; an empty response, a non-200, and a timeout each produce a
      stage that contributed nothing [The service has no record of the invoice, The service cannot be
      reached].
- [x] 4.1 Add the port for retrieving an invoice by decoded identity, alongside `IFiscalCodeDecoder`.
- [x] 4.2 Implement the adapter: form-encoded `iic`, `dateTimeCreated`, `tin`; bounded timeout;
      every failure returns nothing rather than throwing (D26).
- [x] 4.3 Map the response to an `ExtractionResult`, taking values verbatim (D27).
- [x] 4.4 Reuse an already-retrieved invoice when extraction is re-run [Retrieval is not repeated
      needlessly].

## 5. Cascade ordering

- [x] 5.0 Write the failing cascade tests: a decoded-and-retrieved invoice that validates leaves the
      placeholder unrun [A deterministic result ends the cascade, The placeholder does not run behind
      a retrieved invoice]; a decode miss reaches the placeholder stages unchanged [A failed decode
      falls through to the probabilistic stages]; provenance names the retrieval stage on every value
      it produced [Stage provenance is recorded].
- [x] 5.1 Add the retrieval stage as `Primary` and demote the placeholder vision stages to
      `Fallback` (D22).
- [x] 5.2 Register the stages in the new order and add the portal options to configuration.

## 6. Validation precision

- [x] 6.0 Write the failing validation test: lines summing raw to `59.6515` against a stated total of
      `59.65` pass the sum check [Line amounts stated at a greater precision than the total]. This
      must fail against today's validator.
- [x] 6.1 Round each side to two decimal places before comparison, in the sum, discount and tax
      checks alike (D25).

## 7. End to end

- [x] 7.0 Write the failing journey test: uploading `1000023219.jpg` to a purchase yields candidates
      matching the committed recording, the merchant `MEGAPROMET d.o.o`, a total of `59.65`, and an
      `Extracted` state reached without any placeholder candidate. Uploading `1000023157.jpg` reaches
      the placeholder path and is reported no differently from a receipt carrying no code
      [A miss does not degrade the receipt].
- [x] 7.1 Make it pass.
- [x] 7.2 Update the extraction MCP tool and HTTP responses to surface the retrieval stage as the
      source where it produced the values.

## 8. Documentation

No behaviour — no tests.

- [x] 8.1 Record the measured decoder comparison and the observed portal contract in the repository,
      so the next person does not repeat the spike.
- [x] 8.2 Note the open question this change does not answer: whether the portal still serves an old
      receipt, which decides if the deterministic path is permanent or only recent.
