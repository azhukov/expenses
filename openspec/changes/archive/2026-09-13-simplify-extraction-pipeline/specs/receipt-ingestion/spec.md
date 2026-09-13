## ADDED Requirements

### Requirement: The fiscal QR payload is retained with the receipt

Where a fiscal QR payload is obtained for a receipt — supplied by the client at capture or decoded
from the image — the system SHALL retain the payload verbatim alongside the receipt, and SHALL
retain it whether or not any identifier could be parsed out of it. Where a payload is retained, a
later extraction of the same receipt SHALL use it rather than attempting to decode the image again.
A receipt for which no payload was obtained SHALL record its absence rather than an empty payload.

#### Scenario: A supplied payload is retained

- **WHEN** a receipt is captured with a fiscal QR payload and the capture is confirmed
- **THEN** the payload is retained with the receipt exactly as it was submitted

#### Scenario: A decoded payload is retained

- **WHEN** no payload was supplied and one is decoded from the image
- **THEN** the decoded payload is retained with the receipt

#### Scenario: A re-run does not decode the image again

- **WHEN** extraction is re-run for a receipt that retained a fiscal QR payload
- **THEN** the retained payload is used
- **AND** no attempt is made to decode the image

#### Scenario: An unrecognised payload is still retained

- **WHEN** a payload is obtained whose format yields no recognisable identifiers
- **THEN** the payload is still retained with the receipt

#### Scenario: A receipt with no payload

- **WHEN** a receipt is confirmed for which no payload was supplied or decoded
- **THEN** the receipt records that no payload is held

## MODIFIED Requirements

### Requirement: Extraction runs as an ordered cascade of stages

Extraction SHALL be composed of ordered steps rather than a single engine, and each step SHALL take
the same input — the image, its content type, and the fiscal identity known so far — and SHALL
produce the same kind of result. A step SHALL run only when every step before it produced no
candidate lines; a step that produces candidate lines SHALL end the run. Whether a result reconciles
arithmetically SHALL NOT determine which steps run. Free deterministic steps SHALL run before paid
steps, and deterministic steps SHALL be preferred over probabilistic ones for any value both could
produce. Where a step produces no candidate lines but establishes fiscal identity, that identity
SHALL be passed to the next step. The system SHALL record, for every extraction result, which steps
ran and which step produced each extracted value. Where no step produces candidate lines, the
extraction SHALL fail and the reason SHALL be reported to the user.

#### Scenario: Step provenance is recorded

- **WHEN** an extraction result is retrieved
- **THEN** it reports which steps ran
- **AND** each extracted value identifies the step that produced it

#### Scenario: A step that produces lines ends the run

- **WHEN** the first step produces candidate lines
- **THEN** no later step runs

#### Scenario: A step that produces nothing advances the run

- **WHEN** a step produces no candidate lines
- **THEN** the next step runs
- **AND** the outcome is the same as if that step had not been configured

#### Scenario: A result that does not reconcile does not advance the run

- **WHEN** a step produces candidate lines whose amounts do not sum to its total
- **THEN** no later step runs
- **AND** the result is retained for review

#### Scenario: Fiscal identity outlives the step that established it

- **WHEN** a step establishes fiscal identity but produces no candidate lines
- **THEN** that identity is passed to the next step
- **AND** it is retained on the result the later step produces

#### Scenario: No step produces anything

- **WHEN** no step produces candidate lines for an image
- **THEN** the extraction state is Failed
- **AND** the reason is reported to the user

#### Scenario: A failed decode falls through to the probabilistic step

- **WHEN** no fiscal code can be decoded from an image and none was supplied
- **THEN** the probabilistic step runs as it would for a receipt carrying no code

### Requirement: An extraction result is validated arithmetically

The system SHALL check an extraction result against itself using only the values the result
contains. Validation SHALL be performed once, after extraction has produced a result, and SHALL
determine only whether that result is reported as Extracted or as needing review; it SHALL NOT
determine which extraction steps run. The checks SHALL be: that extracted line amounts sum to the
extracted total; that for any line carrying a list price and a discount, the list price less the
discount equals the line amount; and that where a tax rate and tax amount were extracted, the total
implies the extracted tax amount. Amounts SHALL be compared at the precision the ledger records them
in, rounding each side to two decimal places before comparison, so that a source which states line
amounts at a greater precision than its own total is not reported as disagreeing with itself. Each
check SHALL be reported individually, naming the values that disagree. Validation SHALL apply to the
extraction result alone and SHALL NOT alter the purchase.

#### Scenario: A result that adds up

- **WHEN** an extraction produces lines of 4.49 and 3.99 with a total of 8.48
- **THEN** the sum check passes

#### Scenario: A result that does not add up

- **WHEN** an extraction produces lines of 4.49 and 3.99 with a total of 8.98
- **THEN** the sum check fails
- **AND** the failure reports the extracted total 8.98 and the computed sum 8.48

#### Scenario: Validation runs once for a result

- **WHEN** an extraction result is produced by any step
- **THEN** it is validated once
- **AND** the outcome of that validation decides only whether the state is Extracted or NeedsReview

#### Scenario: Discount arithmetic is checked per line

- **WHEN** an extraction produces a line with list price 8.50, discount 4.01 and amount 4.49
- **THEN** the discount check passes for that line

#### Scenario: Discount arithmetic fails for one line only

- **WHEN** an extraction produces one line whose list price less discount equals its amount and one where it does not
- **THEN** the discount check fails
- **AND** the failure identifies which line disagreed

#### Scenario: Tax is checked against the total

- **WHEN** an extraction produces a total of 8.48 at a tax rate of 21 percent and a tax amount of 1.47
- **THEN** the tax check passes

#### Scenario: Checks that cannot be performed are not failures

- **WHEN** an extraction produces no list prices, no discounts and no tax values
- **THEN** the discount and tax checks are reported as not applicable
- **AND** the result is not marked as failing validation on their account

### Requirement: Vision extraction is pluggable

Vision extraction SHALL be a single step, sitting behind a boundary so that it can be replaced
without changing how purchases, images, candidates, validation or the pipeline behave. It SHALL be
the last step, and SHALL run only where the deterministic step produced no candidate lines. It SHALL
receive the fiscal identity established so far, so that a known-true total and issuer identity are
available to it even where the verification service could not be reached. Until a real engine is
introduced the step SHALL remain a placeholder that produces deterministic results and performs no
image analysis. The system SHALL make clear, wherever candidates are surfaced, that they came from a
placeholder engine.

#### Scenario: Placeholder produces deterministic candidates

- **WHEN** the same receipt image is extracted twice by the placeholder engine
- **THEN** both runs produce identical candidate lines

#### Scenario: Placeholder results are identified

- **WHEN** candidate lines produced by the placeholder engine are retrieved
- **THEN** the response identifies the engine that produced them

#### Scenario: Placeholder can produce each terminal state

- **WHEN** the placeholder engine is configured to simulate a low-confidence result or a failure
- **THEN** the image enters NeedsReview or Failed respectively
- **AND** the behaviour matches what a real engine reaching that state would produce

#### Scenario: The placeholder does not run behind a retrieved invoice

- **WHEN** a receipt's invoice is retrieved from the verification service
- **THEN** no placeholder candidates are produced for that receipt
- **AND** this holds whether or not the retrieved invoice reconciles

#### Scenario: The vision step is told what the code established

- **WHEN** a fiscal code yielded a total and an issuer tax number but no invoice could be retrieved
- **THEN** the vision step receives those values
- **AND** they are retained on the result it produces

### Requirement: Fiscal receipt identity is captured when present

Where a receipt carries fiscal identifiers issued by a tax authority, the system SHALL retain them
alongside the image exactly as read, without imposing a format. A fiscal QR payload SHALL be
accepted alongside a capture as well as decoded from the image, so that a client which read the code
at capture time need not depend on the server rediscovering it. Where a payload is supplied, it
SHALL be preferred outright and the image SHALL NOT be decoded for a second reading, because a
client reading a live camera has retries and focus available to it that a single stored frame does
not. Each identifier SHALL be taken only from a source that actually carries it: an identifier
absent from the fiscal code SHALL NOT be populated from another value found there.

#### Scenario: Identifiers read from the receipt

- **WHEN** extraction reads fiscal identifiers from a captured image
- **THEN** they are retained with the image and returned in the capture response, and again when the confirmed purchase's receipt is retrieved

#### Scenario: A payload supplied with the capture

- **WHEN** a receipt image is captured together with a fiscal QR payload
- **THEN** the identifiers it carries are retained with the capture without extraction needing to rediscover them
- **AND** the image is not decoded for a second reading

#### Scenario: Receipt carries no fiscal identifiers

- **WHEN** a receipt with no fiscal identifiers is captured
- **THEN** the image is stored temporarily and extracted normally
- **AND** it reports no fiscal identifiers

#### Scenario: An identifier the fiscal code does not carry

- **WHEN** a fiscal code is decoded that carries an invoice identification code and a creation timestamp but no issuer verification code
- **THEN** the issuer verification code is reported as not yet known
- **AND** the creation timestamp is not recorded as the issuer verification code

#### Scenario: The missing identifier arrives from the verification service

- **WHEN** an invoice retrieved from the verification service carries the issuer verification code
- **THEN** it is retained with the capture
- **AND** it is recorded as having come from the verification service

### Requirement: Fiscal-code decoding is opportunistic

Where no fiscal QR payload was supplied with a capture, the system SHALL attempt to decode one from
the stored receipt image, and SHALL treat failure to decode as an ordinary outcome rather than an
error. Failure SHALL NOT change the extraction state, SHALL NOT be surfaced to the user as a problem
with the receipt, and SHALL NOT prevent the remaining step from running. Where a payload is obtained
by either route, it SHALL lead extraction rather than merely annotate it: no probabilistic step
SHALL be asked to produce a value the code has already established. Decoding SHALL be attempted with
a decoder capable of reading a dense symbol printed on thermal paper and photographed, and the
expectation SHALL be that some tills produce symbols that cannot be read at all.

#### Scenario: Decoding succeeds

- **WHEN** a fiscal code is decoded from a stored image
- **THEN** the identifiers it carries are retained
- **AND** they are marked as having been decoded rather than read as text

#### Scenario: Decoding is skipped when a payload was supplied

- **WHEN** a capture supplies a fiscal QR payload
- **THEN** the image is not decoded
- **AND** the identifiers are marked as having been supplied at capture

#### Scenario: Decoding fails

- **WHEN** no fiscal code can be decoded from a stored image
- **THEN** extraction proceeds unchanged
- **AND** no error is reported to the user
- **AND** the extraction state is unaffected

#### Scenario: Image carries no fiscal code at all

- **WHEN** an image containing no fiscal code is ingested
- **THEN** the outcome is indistinguishable from a fiscal code that could not be decoded

#### Scenario: A decodable symbol is decoded from an unmodified photograph

- **WHEN** a receipt whose symbol is within the decoder's reach is photographed and ingested at full resolution
- **THEN** the fiscal code is decoded
- **AND** decoding did not depend on the image having been cropped or rectified first

#### Scenario: A miss does not degrade the receipt

- **WHEN** a receipt whose symbol is too dense for its print quality is ingested
- **THEN** the receipt is stored and extracted by the remaining step
- **AND** the outcome is reported no differently from a receipt that carries no code

### Requirement: The fiscal QR carries invoice identity and total

Where a fiscal QR payload is obtained for a receipt, whether supplied at capture or decoded from the
image, the system SHALL read the invoice identity it carries without any network call. The payload
SHALL be parsed by one implementation regardless of which route it arrived by, so that a supplied
payload and a decoded one yield the same identifiers. The payload SHALL yield at least the issuer
tax identification number, the invoice creation timestamp, the invoice identification code, and the
invoice total. These values SHALL be treated as exact rather than estimated, and SHALL be recorded
as having been decoded or supplied rather than read as text. A payload SHALL never be dereferenced
as a network address, whatever form it takes.

#### Scenario: Identity and total are read from the code alone

- **WHEN** a fiscal QR payload is obtained for a stored image
- **THEN** the issuer tax identification number, creation timestamp, invoice identification code and total are available
- **AND** no network call was required to obtain them

#### Scenario: A supplied payload yields the same identifiers as a decoded one

- **WHEN** the same payload is supplied at capture and decoded from the image
- **THEN** the identifiers read from it are identical

#### Scenario: The payload is not fetched

- **WHEN** a payload takes the form of a verification address
- **THEN** the identifiers are read from the address without it being requested

#### Scenario: The decoded total anchors validation

- **WHEN** a fiscal QR yields a total and a later step produces line amounts
- **THEN** the line amounts are checked against the decoded total
- **AND** a disagreement is reported against the decoded value rather than against a value the later step produced

#### Scenario: A decoded value is never overwritten by an estimate

- **WHEN** a value decoded from the fiscal QR is also produced by a later probabilistic step
- **THEN** the decoded value is retained
- **AND** the disagreement is reported

### Requirement: Invoice detail is retrieved from the fiscal verification portal

Where a fiscal QR payload has been obtained, the system SHALL retrieve the corresponding invoice
from the fiscal verification service the code refers to, and SHALL map the returned invoice onto
extraction candidates. All receipts are assumed to be issued under the same national fiscalisation
scheme and verified by the same service. A retrieved invoice SHALL be treated as authoritative: its
line items, quantities, units, tax rates, seller identity and totals SHALL be used verbatim rather
than re-estimated, and SHALL NOT be re-derived by any later step, whether or not it reconciles
arithmetically.

#### Scenario: Invoice detail is retrieved and mapped

- **WHEN** an invoice is retrieved for a fiscal code
- **THEN** each returned line becomes a candidate carrying its description, quantity, unit, tax rate and amount
- **AND** the seller name and tax identification number are recorded as the merchant
- **AND** the candidates identify the retrieval step as their source

#### Scenario: A retrieved invoice that does not reconcile is still authoritative

- **WHEN** an invoice retrieved from the verification service fails an arithmetic check
- **THEN** it is retained as the result
- **AND** no probabilistic step is asked to produce an alternative
- **AND** the extraction state is NeedsReview

#### Scenario: The service has no record of the invoice

- **WHEN** the verification service reports no invoice for a fiscal code
- **THEN** extraction continues with the remaining step
- **AND** the identifiers read from the code are still retained

#### Scenario: The service cannot be reached

- **WHEN** the verification service cannot be reached or does not answer within its time budget
- **THEN** the outcome is the same as a step that produced nothing
- **AND** no error about the receipt is reported to the user
- **AND** the values already read from the fiscal QR are retained

#### Scenario: Retrieval is not repeated needlessly

- **WHEN** extraction is re-run for a receipt whose invoice was already retrieved
- **THEN** the retrieved invoice is reused rather than requested again
