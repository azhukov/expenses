## ADDED Requirements

### Requirement: The fiscal QR carries invoice identity and total

Where a receipt's fiscal QR decodes, the system SHALL read the invoice identity it carries without any network call. The decoded payload SHALL yield at least the issuer tax identification number, the invoice creation timestamp, the invoice identification code, and the invoice total. These values SHALL be treated as exact rather than estimated, and SHALL be recorded as having been decoded rather than read as text.

#### Scenario: Identity and total are read from the code alone

- **WHEN** a fiscal QR is decoded from a stored image
- **THEN** the issuer tax identification number, creation timestamp, invoice identification code and total are available
- **AND** no network call was required to obtain them

#### Scenario: The decoded total anchors validation

- **WHEN** a fiscal QR yields a total and a later stage produces line amounts
- **THEN** the line amounts are checked against the decoded total
- **AND** a disagreement is reported against the decoded value rather than against a value the later stage produced

#### Scenario: A decoded value is never overwritten by an estimate

- **WHEN** a value decoded from the fiscal QR is also produced by a later probabilistic stage
- **THEN** the decoded value is retained
- **AND** the disagreement is reported

### Requirement: Invoice detail is retrieved from the fiscal verification portal

Where a fiscal QR has been decoded, the system SHALL retrieve the corresponding invoice from the fiscal verification service the code refers to, and SHALL map the returned invoice onto extraction candidates. All receipts are assumed to be issued under the same national fiscalisation scheme and verified by the same service. A retrieved invoice SHALL be treated as authoritative: its line items, quantities, units, tax rates, seller identity and totals SHALL be used verbatim rather than re-estimated.

#### Scenario: Invoice detail is retrieved and mapped

- **WHEN** an invoice is retrieved for a decoded fiscal code
- **THEN** each returned line becomes a candidate carrying its description, quantity, unit, tax rate and amount
- **AND** the seller name and tax identification number are recorded as the merchant
- **AND** the candidates identify the retrieval stage as their source

#### Scenario: Retrieved detail supersedes an estimate

- **WHEN** an invoice is retrieved for a receipt that a probabilistic stage had already described
- **THEN** the retrieved values are the ones retained
- **AND** the result records that they came from the verification service

#### Scenario: The service has no record of the invoice

- **WHEN** the verification service reports no invoice for a decoded fiscal code
- **THEN** extraction continues with the remaining stages
- **AND** the identifiers decoded from the code are still retained

#### Scenario: The service cannot be reached

- **WHEN** the verification service cannot be reached or does not answer within its time budget
- **THEN** the outcome is the same as a stage that produced nothing
- **AND** no error about the receipt is reported to the user
- **AND** the values already decoded from the fiscal QR are retained

#### Scenario: Retrieval is not repeated needlessly

- **WHEN** extraction is re-run for a receipt whose invoice was already retrieved
- **THEN** the retrieved invoice is reused rather than requested again

## MODIFIED Requirements

### Requirement: Fiscal-code decoding is opportunistic

The system SHALL attempt to decode a fiscal code from a stored receipt image, and SHALL treat failure to decode as an ordinary outcome rather than an error. Failure SHALL NOT change the extraction state, SHALL NOT be surfaced to the user as a problem with the receipt, and SHALL NOT prevent any later stage from running. Where the code does decode, it SHALL lead extraction rather than merely annotate it: no probabilistic stage SHALL be asked to produce a value the code has already established. Decoding SHALL be attempted with a decoder capable of reading a dense symbol printed on thermal paper and photographed, and the expectation SHALL be that some tills produce symbols that cannot be read at all.

#### Scenario: Decoding succeeds

- **WHEN** a fiscal code is decoded from a stored image
- **THEN** the identifiers it carries are retained
- **AND** they are marked as having been decoded rather than read as text

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
- **THEN** the receipt is stored and extracted by the remaining stages
- **AND** the outcome is reported no differently from a receipt that carries no code

### Requirement: Extraction runs as an ordered cascade of stages

Extraction SHALL be composed of ordered stages rather than a single engine. Free deterministic stages SHALL run before paid stages, and deterministic stages SHALL be preferred over probabilistic ones for any value both could produce. The system SHALL record, for every extraction result, which stages ran and which stage produced each extracted value. A stage that produces no result SHALL NOT fail the extraction, and every later stage SHALL behave identically whether or not an earlier optional stage produced anything.

#### Scenario: Stage provenance is recorded

- **WHEN** an extraction result is retrieved
- **THEN** it reports which stages ran
- **AND** each extracted value identifies the stage that produced it

#### Scenario: Optional stage produces nothing

- **WHEN** an optional stage produces no result for an image
- **THEN** extraction continues with the remaining stages
- **AND** the outcome is the same as if that stage had not been configured

#### Scenario: The expensive stage runs only on demand

- **WHEN** an extraction result passes arithmetic validation
- **THEN** the fallback extraction stage does not run

#### Scenario: The expensive stage runs after a failed validation

- **WHEN** an extraction result fails arithmetic validation
- **THEN** the fallback extraction stage runs
- **AND** its result is validated in turn

#### Scenario: Fallback also fails

- **WHEN** the fallback stage runs and its result also fails arithmetic validation
- **THEN** the image enters the NeedsReview state
- **AND** the result retained is the one that failed fewer checks
- **AND** both results remain distinguishable by the stage that produced them

#### Scenario: A deterministic result ends the cascade

- **WHEN** the verification service returns an invoice that passes arithmetic validation
- **THEN** no probabilistic stage runs for that image
- **AND** the result records that it was produced deterministically

#### Scenario: A failed decode falls through to the probabilistic stages

- **WHEN** no fiscal code can be decoded from an image
- **THEN** the probabilistic stages run as they would for a receipt carrying no code

### Requirement: An extraction result is validated arithmetically

The system SHALL check an extraction result against itself before accepting it, using only the values the result contains. The checks SHALL be: that extracted line amounts sum to the extracted total; that for any line carrying a list price and a discount, the list price less the discount equals the line amount; and that where a tax rate and tax amount were extracted, the total implies the extracted tax amount. Amounts SHALL be compared at the precision the ledger records them in, rounding each side to two decimal places before comparison, so that a source which states line amounts at a greater precision than its own total is not reported as disagreeing with itself. Each check SHALL be reported individually, naming the values that disagree. Validation SHALL apply to the extraction result alone and SHALL NOT alter the purchase.

#### Scenario: A result that adds up

- **WHEN** an extraction produces lines of 4.49 and 3.99 with a total of 8.48
- **THEN** the sum check passes

#### Scenario: A result that does not add up

- **WHEN** an extraction produces lines of 4.49 and 3.99 with a total of 8.98
- **THEN** the sum check fails
- **AND** the failure reports the extracted total 8.98 and the computed sum 8.48

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

#### Scenario: Validation does not touch the ledger

- **WHEN** an extraction result fails every check
- **THEN** the expenses of the purchase are unchanged
- **AND** the candidates remain available for review

#### Scenario: Line amounts stated at a greater precision than the total

- **WHEN** an extraction produces lines whose raw sum is 59.6515 against a stated total of 59.65
- **THEN** the sum check passes
- **AND** the result is not marked as needing review on account of the rounding

### Requirement: Fiscal receipt identity is captured when present

Where a receipt carries fiscal identifiers issued by a tax authority, the system SHALL retain them alongside the image exactly as read, without imposing a format. Fiscal identifiers SHALL be accepted alongside an upload as well as discovered from the image, so that a client which decodes them at capture need not depend on the server rediscovering them. Each identifier SHALL be taken only from a source that actually carries it: an identifier absent from the fiscal code SHALL NOT be populated from another value found there. Where an identifier is known from more than one source, the system SHALL compare them and SHALL report a disagreement rather than silently preferring one.

#### Scenario: Identifiers read from the receipt

- **WHEN** extraction reads fiscal identifiers from a receipt
- **THEN** they are retained with the image and returned when it is retrieved

#### Scenario: Identifiers supplied with the upload

- **WHEN** a receipt image is uploaded together with fiscal identifiers decoded by the client
- **THEN** they are retained with the image without extraction having run

#### Scenario: Sources agree

- **WHEN** a fiscal identifier supplied at upload matches the one later read by extraction
- **THEN** the identifier is reported as corroborated

#### Scenario: Sources disagree

- **WHEN** a fiscal identifier supplied at upload differs from the one later read by extraction
- **THEN** both values are retained
- **AND** the disagreement is reported
- **AND** the image enters the NeedsReview state

#### Scenario: Receipt carries no fiscal identifiers

- **WHEN** a receipt with no fiscal identifiers is ingested
- **THEN** the image is stored and extracted normally
- **AND** it reports no fiscal identifiers

#### Scenario: An identifier the fiscal code does not carry

- **WHEN** a fiscal code is decoded that carries an invoice identification code and a creation timestamp but no issuer verification code
- **THEN** the issuer verification code is reported as not yet known
- **AND** the creation timestamp is not recorded as the issuer verification code

#### Scenario: The missing identifier arrives from the verification service

- **WHEN** an invoice retrieved from the verification service carries the issuer verification code
- **THEN** it is retained with the image
- **AND** it is recorded as having come from the verification service

### Requirement: Vision extraction is pluggable and mocked in this change

The vision extraction stages SHALL sit behind a boundary so that they can be replaced without changing how purchases, images, candidates, validation or the cascade behave. Vision stages SHALL be the fallback for receipts the deterministic path cannot serve, and SHALL NOT run for a receipt whose invoice was retrieved and validated. Until a real engine is introduced those stages SHALL remain placeholders that produce deterministic results and perform no image analysis. The system SHALL make clear, wherever candidates are surfaced, that they came from a placeholder engine.

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

- **WHEN** a receipt's invoice is retrieved from the verification service and passes validation
- **THEN** no placeholder candidates are produced for that receipt
