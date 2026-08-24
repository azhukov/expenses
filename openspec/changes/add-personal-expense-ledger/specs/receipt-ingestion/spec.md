## Purpose

Defines how a receipt image is uploaded, attached to a purchase, and stored; how the fiscal identity a receipt carries is captured; and how the system turns that image into candidate expense lines through an ordered cascade of extraction stages whose result is validated arithmetically before a user reviews and confirms it.

## ADDED Requirements

### Requirement: A purchase carries at most one receipt image

A purchase SHALL have zero or one receipt image. A purchase created by manual entry SHALL have none. The system SHALL reject an attempt to attach a second image to a purchase that already has one, and SHALL leave the existing image untouched.

#### Scenario: Attach an image to a purchase

- **WHEN** a receipt image is uploaded for a purchase that has no image
- **THEN** the image is stored and attached to that purchase

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

### Requirement: The same image file is not stored twice

The system SHALL detect when uploaded bytes are identical to those of an image already stored, and SHALL reuse the stored image rather than storing a second copy. Reuse SHALL NOT prevent the image from being attached to a different purchase.

#### Scenario: Identical bytes uploaded again

- **WHEN** an image is uploaded whose bytes are identical to an already stored image
- **THEN** the stored image is reused
- **AND** only one copy of the bytes is retained

#### Scenario: Re-photographed receipt

- **WHEN** a second, visually similar but not byte-identical photograph of the same paper receipt is uploaded
- **THEN** it is treated as a new image and stored

### Requirement: Extraction lifecycle

Every attached receipt image SHALL have an extraction state, and that state SHALL be observable. The states SHALL be Pending, Extracting, Extracted, NeedsReview and Failed. Extraction SHALL NOT block the upload response.

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

### Requirement: Verbatim receipt text is preserved

For every extracted line, the system SHALL retain the category and unit text exactly as it appeared on the receipt, in addition to any reference data value it was matched to. Verbatim text SHALL be retained even when no match is found, and SHALL survive later re-matching.

#### Scenario: Unit text is preserved alongside a match

- **WHEN** a line reading "Bund" is extracted and matched to a known unit
- **THEN** both the matched unit and the verbatim text "Bund" are retained

#### Scenario: Unmatched text is still preserved

- **WHEN** a line reading "Pfand" is extracted and matches no known unit
- **THEN** the verbatim text "Pfand" is retained
- **AND** the line has no matched unit

#### Scenario: Non-Latin receipt text

- **WHEN** a receipt line containing non-Latin characters is extracted
- **THEN** the text is retained unchanged, character for character

### Requirement: Vision extraction is pluggable and mocked in this change

The vision extraction stages SHALL sit behind a boundary so that they can be replaced without changing how purchases, images, candidates, validation or the cascade behave. In this change those stages SHALL be placeholders that produce deterministic results and perform no image analysis. The system SHALL make clear, wherever candidates are surfaced, that they came from a placeholder engine.

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

Extraction SHALL be composed of ordered stages rather than a single engine. Free deterministic stages SHALL run before paid stages. The system SHALL record, for every extraction result, which stages ran and which stage produced each extracted value. A stage that produces no result SHALL NOT fail the extraction, and every later stage SHALL behave identically whether or not an earlier optional stage produced anything.

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

### Requirement: An extraction result is validated arithmetically

The system SHALL check an extraction result against itself before accepting it, using only the values the result contains. The checks SHALL be: that extracted line amounts sum to the extracted total; that for any line carrying a list price and a discount, the list price less the discount equals the line amount; and that where a tax rate and tax amount were extracted, the total implies the extracted tax amount. Each check SHALL be reported individually, naming the values that disagree. Validation SHALL apply to the extraction result alone and SHALL NOT alter the purchase.

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

### Requirement: Fiscal receipt identity is captured when present

Where a receipt carries fiscal identifiers issued by a tax authority, the system SHALL retain them alongside the image exactly as read, without imposing a format. Fiscal identifiers SHALL be accepted alongside an upload as well as discovered from the image, so that a client which decodes them at capture need not depend on the server rediscovering them. Where an identifier is known from more than one source, the system SHALL compare them and SHALL report a disagreement rather than silently preferring one.

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

### Requirement: Fiscal-code decoding is opportunistic

The system SHALL attempt to decode a fiscal code from a stored receipt image, and SHALL treat failure to decode as an ordinary outcome rather than an error. Failure SHALL NOT change the extraction state, SHALL NOT be surfaced to the user as a problem with the receipt, and SHALL NOT prevent any later stage from running.

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
