# receipt-ingestion Specification

## Purpose

Defines how a receipt image is uploaded, attached to a purchase, and stored as a file the ledger refers to; how the fiscal identity a receipt carries is captured; and how the system turns that image into candidate expense lines — held only until they are confirmed or discarded — through an ordered cascade of extraction stages whose result is validated arithmetically before a user reviews and confirms it.

## Requirements

### Requirement: A purchase carries at most one receipt image

A purchase SHALL have zero or one receipt image. A purchase created by manual entry SHALL have none. The system SHALL reject an attempt to attach a second image to a purchase that already has one, and SHALL leave the existing image untouched. A receipt SHALL belong to exactly one purchase and SHALL have no identity of its own: every operation on a receipt — uploading it, retrieving its bytes, reading its extraction state, re-running extraction — SHALL address it by the purchase that carries it. An image SHALL NOT exist in the system unattached.

#### Scenario: Attach an image to a purchase

- **WHEN** a receipt image is uploaded for a purchase that has no image
- **THEN** the image is stored and attached to that purchase

#### Scenario: Upload for a purchase that does not exist

- **WHEN** a receipt image is uploaded for a purchase identifier that matches no purchase
- **THEN** the system rejects the upload and reports that the purchase was not found
- **AND** nothing is stored

#### Scenario: A receipt is addressed through its purchase

- **WHEN** the receipt of a purchase is retrieved, its extraction state read, or its extraction re-run
- **THEN** the purchase identifies it in every case
- **AND** no separate image identifier is required or exposed

#### Scenario: Attach a second image

- **WHEN** a receipt image is uploaded for a purchase that already has one
- **THEN** the system rejects the request and reports that the purchase already has a receipt image
- **AND** the existing image is unchanged

#### Scenario: Manual purchase has no image

- **WHEN** a purchase is recorded by manual entry
- **THEN** that purchase has no receipt image

### Requirement: Uploaded images are validated

The system SHALL accept receipt images in JPEG, PNG, WebP and HEIC formats, and SHALL accept PDF documents. The system SHALL reject uploads exceeding 15 megabytes. The system SHALL determine the content type from the file content rather than trusting the declared type or file extension.

#### Scenario: Accepted format

- **WHEN** a 2 megabyte JPEG is uploaded
- **THEN** the upload succeeds

#### Scenario: Rejected format

- **WHEN** a file that is not one of the accepted formats is uploaded
- **THEN** the system rejects the upload and states which formats are accepted
- **AND** nothing is stored

#### Scenario: Oversized file

- **WHEN** a file larger than 15 megabytes is uploaded
- **THEN** the system rejects the upload and states the size limit

#### Scenario: Declared type disagrees with content

- **WHEN** a file whose content is not an accepted format is uploaded while declaring itself to be a JPEG
- **THEN** the system rejects the upload

### Requirement: The same bytes are not stored twice

The system SHALL detect when uploaded bytes are identical to those of an image already stored, and SHALL retain a single copy of them rather than a second. Sharing one stored copy SHALL NOT be observable to either purchase: each SHALL carry its own receipt, its own extraction state and its own fiscal identifiers, and neither SHALL be affected by what happens to the other.

#### Scenario: Identical bytes uploaded again

- **WHEN** an image is uploaded for a second purchase whose bytes are identical to one already stored
- **THEN** only one copy of the bytes is retained
- **AND** both purchases carry a receipt of their own

#### Scenario: Sharing is not visible in behaviour

- **WHEN** two purchases carry receipts with identical bytes and extraction is re-run for one of them
- **THEN** only that purchase's extraction state and candidates change
- **AND** the other purchase is unaffected

#### Scenario: Re-photographed receipt

- **WHEN** a second, visually similar but not byte-identical photograph of the same paper receipt is uploaded
- **THEN** it is treated as a new image and stored
- **AND** a second file is written for it

### Requirement: Receipt bytes are stored as files the purchase refers to

The system SHALL store the bytes of a receipt in a file under a configured receipt store, and SHALL record against the purchase only the reference to that file together with the content type and size of the image. The reference SHALL be derived from the content of the file, so that it is also the identity of that content and byte-identical receipts share one reference. The ledger SHALL NOT contain the bytes themselves. The reference SHALL be retained as recorded rather than recomputed, so that the layout of the store can change without invalidating existing references. The system SHALL write the file before recording the reference, and SHALL clear the reference before deleting the file, so that a reference to an absent file is never created by an interruption.

#### Scenario: Stored bytes are outside the ledger

- **WHEN** a receipt image is uploaded for a purchase
- **THEN** its bytes are written to a file in the receipt store
- **AND** the purchase records the reference to that file, its content type and size, and not its bytes

#### Scenario: A purchase either carries a whole receipt or none

- **WHEN** a purchase is retrieved
- **THEN** either it carries a file reference, content type, size and extraction state together, or it carries none of them
- **AND** no partial receipt is ever recorded

#### Scenario: Interrupted upload leaves no broken reference

- **WHEN** an upload is interrupted after the file is written and before the reference is recorded
- **THEN** the purchase carries no receipt
- **AND** the ledger contains no reference to that file

#### Scenario: Interrupted deletion leaves no broken reference

- **WHEN** a receipt is deleted and the process is interrupted after the reference is cleared and before the file is
- **THEN** the ledger contains no reference to that file
- **AND** the remaining file is unreferenced and safe to discard

#### Scenario: Deleting a receipt whose bytes another purchase shares

- **WHEN** the receipt of one purchase is deleted while another purchase references the same file
- **THEN** the file is retained
- **AND** the other purchase's receipt can still be retrieved

#### Scenario: Deleting the last receipt referencing a file

- **WHEN** the receipt of the only purchase referencing a file is deleted
- **THEN** the file is removed from the receipt store

#### Scenario: Deleting a purchase takes its receipt reference with it

- **WHEN** a purchase carrying a receipt is deleted
- **THEN** nothing referring to that receipt remains in the ledger
- **AND** its file is dealt with under the same sharing rule

#### Scenario: Receipt store is unavailable at startup

- **WHEN** the configured receipt store is missing or cannot be written to
- **THEN** the system refuses to start and states which location it could not use

#### Scenario: A referenced file is missing

- **WHEN** the bytes of a stored image are requested and the file it refers to is not present
- **THEN** the system reports the image as unavailable rather than as a server fault
- **AND** the purchase, its expenses and the image's recorded metadata are unchanged

### Requirement: Extraction lifecycle

Every attached receipt image SHALL have an extraction state, recorded against the purchase that carries it, and that state SHALL be observable. The states SHALL be Pending, Extracting, Extracted, NeedsReview and Failed. A purchase with no receipt SHALL have no extraction state. Extraction SHALL NOT block the upload response.

#### Scenario: State on upload

- **WHEN** a receipt image is uploaded
- **THEN** the upload completes without waiting for extraction
- **AND** the image is in the Pending state

#### Scenario: Successful extraction

- **WHEN** extraction of a Pending image completes, its result passes arithmetic validation, and no unverifiable value falls below the confidence threshold
- **THEN** the image enters the Extracted state
- **AND** candidate expense lines are available for that purchase

#### Scenario: Extraction that does not add up

- **WHEN** extraction completes but its result fails arithmetic validation
- **THEN** the image enters the NeedsReview state
- **AND** the candidate lines are available with the failing checks reported

#### Scenario: Low confidence on a value arithmetic cannot check

- **WHEN** extraction passes arithmetic validation but reports low confidence for a description, merchant name or category guess
- **THEN** the image enters the NeedsReview state
- **AND** the candidate lines are available with those values marked

#### Scenario: Failed extraction

- **WHEN** extraction cannot produce any result
- **THEN** the image enters the Failed state
- **AND** the reason for failure is available
- **AND** the purchase and its image remain intact so the user can enter lines by hand

#### Scenario: Extraction state is readable

- **WHEN** a purchase with a receipt image is retrieved
- **THEN** the extraction state of its image is included

### Requirement: Extracted lines are candidates until confirmed

Extraction SHALL NOT alter the expenses of a purchase directly. Extracted values SHALL be presented as candidates that a user confirms, edits or discards. Only on confirmation SHALL they become expenses of the purchase, and the reconciliation rule between purchase amount and expense amounts SHALL apply at that point.

#### Scenario: Candidates do not change the ledger

- **WHEN** extraction of a receipt image completes
- **THEN** the expenses of the purchase are unchanged
- **AND** the extracted lines are available separately as candidates

#### Scenario: Confirming candidates

- **WHEN** a user confirms a set of candidate lines whose amounts sum to the purchase amount
- **THEN** those lines replace the expenses of the purchase

#### Scenario: Confirming candidates that do not reconcile

- **WHEN** a user confirms candidate lines whose amounts do not sum to the purchase amount
- **THEN** the system rejects the confirmation and reports the discrepancy
- **AND** the expenses of the purchase are unchanged

#### Scenario: Discarding candidates

- **WHEN** a user discards the candidates for a receipt image
- **THEN** the candidates are removed
- **AND** the image and the purchase remain

### Requirement: Candidates are transient and are not part of the ledger

Candidate lines and the extraction result that produced them SHALL NOT be persisted in the ledger. They SHALL be held against the purchase whose receipt produced them, and SHALL be available only until they are confirmed, discarded, replaced by a re-run, or the system restarts. What outlives a restart SHALL be the confirmed expenses, and the extraction state, failure reason and fiscal identifiers recorded on the purchase. Candidates being unavailable SHALL be reported as absence rather than as an error, and SHALL NOT be interpreted as an extraction failure. The system SHALL NOT re-run extraction merely because candidates were requested and found absent; re-running SHALL be an explicit action.

#### Scenario: Candidates do not outlive a restart

- **WHEN** an image has unconfirmed candidates and the system is restarted
- **THEN** the candidates are no longer available
- **AND** the extraction state, failure reason and fiscal identifiers recorded for that image are unchanged

#### Scenario: Confirmed lines are unaffected by a restart

- **WHEN** candidates were confirmed into expenses and the system is restarted
- **THEN** those expenses are unchanged
- **AND** the purchase still reconciles

#### Scenario: Requesting candidates that are no longer held

- **WHEN** the candidates of an image whose recorded state is Extracted are requested and none are held
- **THEN** the response reports that no candidates are available, together with the recorded state
- **AND** it is not reported as an extraction failure
- **AND** extraction is not started

#### Scenario: Re-running restores candidates

- **WHEN** extraction is re-run for an image whose candidates are no longer held
- **THEN** candidates are produced again
- **AND** any expenses already confirmed for that purchase are unchanged

#### Scenario: Extraction work still in flight when the system stops

- **WHEN** the system restarts while images are Pending or mid-extraction
- **THEN** those images are extracted again
- **AND** no image is left permanently mid-extraction

### Requirement: Verbatim receipt text is preserved

For every extracted line, the system SHALL retain the category and unit text exactly as it appeared on the receipt, in addition to any reference data value it was matched to. Verbatim text SHALL be retained even when no match is found, and SHALL survive later re-matching. Because a candidate is transient, verbatim text SHALL be carried into the expense when the line is confirmed, so that what the receipt printed is retained by the ledger and not only by the candidate.

#### Scenario: Unit text is preserved alongside a match

- **WHEN** a line reading "Bund" is extracted and matched to a known unit
- **THEN** both the matched unit and the verbatim text "Bund" are retained

#### Scenario: Unmatched text is still preserved

- **WHEN** a line reading "Pfand" is extracted and matches no known unit
- **THEN** the verbatim text "Pfand" is retained
- **AND** the line has no matched unit

#### Scenario: Verbatim text survives confirmation

- **WHEN** a candidate line carrying verbatim category and unit text is confirmed into an expense
- **THEN** the expense retains that text unchanged
- **AND** it remains available after the candidate is gone

#### Scenario: Non-Latin receipt text

- **WHEN** a receipt line containing non-Latin characters is extracted
- **THEN** the text is retained unchanged, character for character

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

### Requirement: Extraction can be re-run

A user SHALL be able to re-run extraction for a stored receipt image. Re-running SHALL replace any existing unconfirmed candidates for that image, and SHALL NOT alter expenses that have already been confirmed.

#### Scenario: Re-run replaces candidates

- **WHEN** extraction is re-run for an image that has unconfirmed candidates
- **THEN** the previous candidates are replaced by the new ones

#### Scenario: Re-run after confirmation

- **WHEN** extraction is re-run for an image whose candidates were already confirmed into expenses
- **THEN** the expenses of the purchase are unchanged
- **AND** the new candidates are offered separately

#### Scenario: Re-run a failed extraction

- **WHEN** extraction is re-run for an image in the Failed state
- **THEN** the image returns to Pending and is extracted again

### Requirement: Stored images can be retrieved

The system SHALL allow retrieving the stored bytes of a receipt image attached to a purchase, together with its content type, so that a client can display it beside the expense lines.

#### Scenario: Retrieve an image

- **WHEN** the receipt image of a purchase is requested
- **THEN** the original bytes are returned with the correct content type

#### Scenario: Retrieve an image that does not exist

- **WHEN** the receipt image of a purchase that has none is requested
- **THEN** the system reports that it was not found

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
