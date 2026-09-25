# receipt-ingestion Specification

## Purpose

Defines how a receipt image is uploaded, attached to a purchase, and stored as a file the ledger refers to; how the fiscal identity a receipt carries is captured; and how the system turns that image into candidate expense lines — held only until they are confirmed or discarded — through an ordered cascade of extraction stages whose result is validated arithmetically before a user reviews and confirms it.

## Requirements

### Requirement: A purchase carries at most one receipt image

A purchase SHALL have zero or one receipt image. A purchase created by manual entry, with no capture behind it, SHALL have none, and a purchase created from a capture that carried only a fiscal QR payload SHALL have none. A receipt SHALL belong to exactly one purchase and SHALL have no identity of its own: every operation on a receipt — retrieving its bytes, reading its extraction state, re-running extraction — SHALL address it by the purchase that carries it. An image SHALL NOT exist attached to a purchase other than through confirming a capture: a purchase acquires its receipt, if any, at the moment it is created, and never afterward.

#### Scenario: A receipt is addressed through its purchase

- **WHEN** the receipt of a purchase is retrieved, its extraction state read, or its extraction re-run
- **THEN** the purchase identifies it in every case
- **AND** no separate image identifier is required or exposed

#### Scenario: Manual purchase has no image

- **WHEN** a purchase is recorded by manual entry with no capture confirmed alongside it
- **THEN** that purchase has no receipt image

#### Scenario: A purchase from a fiscal-only capture has no image

- **WHEN** a purchase is created by confirming a capture that carried a fiscal QR payload and no image
- **THEN** that purchase has no receipt image

#### Scenario: A purchase never gains a receipt after creation

- **WHEN** a purchase already exists, whether or not it carries a receipt
- **THEN** there is no operation that attaches or replaces its receipt
- **AND** its receipt, if any, remains the one it was created with

### Requirement: Uploaded images are validated

The system SHALL accept receipt images in JPEG, PNG, WebP and HEIC formats, and SHALL accept PDF documents, wherever an image is captured. The system SHALL reject captures exceeding 15 megabytes. The system SHALL determine the content type from the file content rather than trusting the declared type or file extension.

#### Scenario: Accepted format

- **WHEN** a 2 megabyte JPEG is captured
- **THEN** the capture succeeds

#### Scenario: Rejected format

- **WHEN** a file that is not one of the accepted formats is captured
- **THEN** the system rejects the capture and states which formats are accepted
- **AND** nothing is stored, even temporarily

#### Scenario: Oversized file

- **WHEN** a file larger than 15 megabytes is captured
- **THEN** the system rejects the capture and states the size limit

#### Scenario: Declared type disagrees with content

- **WHEN** a file whose content is not an accepted format is captured while declaring itself to be a JPEG
- **THEN** the system rejects the capture

### Requirement: The same bytes are not stored twice

The system SHALL detect, when a capture is confirmed, whether its bytes are identical to those of an image already promoted to the permanent receipt store, and SHALL retain a single permanent copy of them rather than a second. Sharing one stored copy SHALL NOT be observable to either purchase: each SHALL carry its own receipt, its own extraction state and its own fiscal identifiers, and neither SHALL be affected by what happens to the other. The temporary store SHALL NOT deduplicate: two captures of identical bytes SHALL each receive their own temporary key, since deduplication is a property of the permanent, content-addressed store alone.

#### Scenario: Identical bytes confirmed for a second purchase

- **WHEN** a capture whose bytes are identical to an already-promoted image is confirmed into a second purchase
- **THEN** only one permanent copy of the bytes is retained
- **AND** both purchases carry a receipt of their own

#### Scenario: Sharing is not visible in behaviour

- **WHEN** two purchases carry receipts with identical bytes and extraction is re-run for one of them
- **THEN** only that purchase's extraction state and candidates change
- **AND** the other purchase is unaffected

#### Scenario: Re-photographed receipt

- **WHEN** a second, visually similar but not byte-identical photograph of the same paper receipt is captured and confirmed
- **THEN** it is treated as a new image and stored as a second file

#### Scenario: Two temporary captures of the same bytes

- **WHEN** the same image bytes are captured twice before either is confirmed
- **THEN** two distinct temporary keys exist
- **AND** confirming one does not affect the other's availability to confirm

### Requirement: Receipt bytes are stored as files the purchase refers to

The system SHALL store the bytes of a receipt in a file under a configured permanent receipt store once a capture is confirmed, and SHALL record against the purchase only the reference to that file together with the content hash, content type and size of the image. The ledger SHALL NOT contain the bytes themselves. The reference SHALL be retained as recorded rather than recomputed, so that the layout of the store can change without invalidating existing references. The system SHALL promote the temporary file into the permanent store before creating the purchase that references it, so that a purchase is never recorded pointing at a file that was never written.

#### Scenario: Stored bytes are outside the ledger

- **WHEN** a capture is confirmed
- **THEN** its bytes are written to a file in the permanent receipt store
- **AND** the created purchase records the reference to that file, its content hash, content type and size, and not its bytes

#### Scenario: A purchase either carries a whole receipt or none

- **WHEN** a purchase is retrieved
- **THEN** either it carries a file reference, content hash, content type, size and extraction state together, or it carries none of them
- **AND** no partial receipt is ever recorded

#### Scenario: Interrupted confirmation leaves no broken reference

- **WHEN** confirmation is interrupted after the file is promoted and before the purchase is recorded
- **THEN** no purchase exists referencing that file
- **AND** the promoted file is unreferenced and safe to discard, or safe to promote again on a retried confirmation

#### Scenario: Interrupted deletion leaves no broken reference

- **WHEN** a receipt is deleted and the process is interrupted after the reference is cleared and before the file is
- **THEN** the ledger contains no reference to that file
- **AND** the remaining file is unreferenced and safe to discard

#### Scenario: Deleting a receipt whose bytes another purchase shares

- **WHEN** the receipt of one purchase is deleted while another purchase references the same content hash
- **THEN** the file is retained
- **AND** the other purchase's receipt can still be retrieved

#### Scenario: Deleting the last receipt referencing a file

- **WHEN** the receipt of the only purchase referencing a file is deleted
- **THEN** the file is removed from the permanent receipt store

#### Scenario: Deleting a purchase takes its receipt reference with it

- **WHEN** a purchase carrying a receipt is deleted
- **THEN** nothing referring to that receipt remains in the ledger
- **AND** its file is dealt with under the same sharing rule

#### Scenario: Receipt store is unavailable at startup

- **WHEN** the configured permanent or temporary receipt store is missing or cannot be written to
- **THEN** the system refuses to start and states which location it could not use

#### Scenario: A referenced file is missing

- **WHEN** the bytes of a stored image are requested and the file it refers to is not present
- **THEN** the system reports the image as unavailable rather than as a server fault
- **AND** the purchase, its expenses and the image's recorded metadata are unchanged

### Requirement: Extraction lifecycle

Every purchase that carries a receipt image or a fiscal identity SHALL have an extraction state, recorded against the purchase, and that state SHALL be observable. A purchase carrying neither SHALL have no extraction state. The states SHALL be Extracted, NeedsReview and Failed. Extraction SHALL run to completion within the request that triggers it — capturing an image or a fiscal QR payload, or re-running extraction for a purchase's receipt — and no Pending or Extracting state SHALL be observable between requests: by the time a capture response or a re-run response is returned, extraction has already reached one of the three terminal states.

#### Scenario: State on capture

- **WHEN** a receipt image or a fiscal QR payload is captured
- **THEN** the response does not return until extraction has completed
- **AND** it reports one of Extracted, NeedsReview or Failed

#### Scenario: Successful extraction

- **WHEN** extraction of a capture completes, its result passes arithmetic validation, and no unverifiable value falls below the confidence threshold
- **THEN** the outcome is Extracted
- **AND** candidate expense lines are available in the response

#### Scenario: Extraction that does not add up

- **WHEN** extraction completes but its result fails arithmetic validation
- **THEN** the outcome is NeedsReview
- **AND** the candidate lines are available with the failing checks reported

#### Scenario: Low confidence on a value arithmetic cannot check

- **WHEN** extraction passes arithmetic validation but reports low confidence for a description, merchant name or category guess
- **THEN** the outcome is NeedsReview
- **AND** the candidate lines are available with those values marked

#### Scenario: Failed extraction

- **WHEN** extraction of an image capture cannot produce any result
- **THEN** the outcome is Failed
- **AND** the reason for failure is available
- **AND** the temporary image remains available so the caller can confirm it with lines entered by hand

#### Scenario: Extraction state is readable

- **WHEN** a purchase with a receipt image or a fiscal identity is retrieved
- **THEN** its extraction state is included, and is one of Extracted, NeedsReview or Failed

#### Scenario: No state without a receipt

- **WHEN** a purchase carrying neither a receipt image nor a fiscal identity is retrieved
- **THEN** it reports no extraction state

### Requirement: Extracted lines are candidates until confirmed

Extraction SHALL NOT alter the expenses of a purchase directly. Extracted values SHALL be presented as candidates that a caller confirms, edits or discards. For a receipt already attached to a purchase, only on confirmation SHALL candidates become expenses of that purchase, and the reconciliation rule between purchase amount and expense amounts SHALL apply at that point. For a capture not yet attached to any purchase, confirmation both creates the purchase and establishes its expenses in the same step; the reconciliation rule applies identically.

#### Scenario: Candidates do not change the ledger

- **WHEN** extraction of an already-attached receipt image is re-run
- **THEN** the expenses of the purchase are unchanged
- **AND** the extracted lines are available separately as candidates

#### Scenario: Confirming candidates for an existing purchase

- **WHEN** a user confirms a set of candidate lines, for a receipt already attached to a purchase, whose amounts sum to the purchase amount
- **THEN** those lines replace the expenses of the purchase

#### Scenario: Confirming candidates that do not reconcile

- **WHEN** a user confirms candidate lines, for a receipt already attached to a purchase, whose amounts do not sum to the purchase amount
- **THEN** the system rejects the confirmation and reports the discrepancy
- **AND** the expenses of the purchase are unchanged

#### Scenario: Discarding candidates

- **WHEN** a user discards the candidates produced by re-running extraction for an existing purchase's receipt
- **THEN** the candidates are removed
- **AND** the image and the purchase remain

### Requirement: Candidates are transient and are not part of the ledger

Candidate lines and the extraction result that produced them, for a receipt already attached to a purchase, SHALL NOT be persisted in the ledger. They SHALL be held against the purchase whose receipt produced them, and SHALL be available only until they are confirmed, discarded, replaced by a re-run, or the system restarts. What outlives a restart SHALL be the confirmed expenses, and the extraction state, failure reason and fiscal identifiers recorded on the purchase. Candidates being unavailable SHALL be reported as absence rather than as an error, and SHALL NOT be interpreted as an extraction failure. The system SHALL NOT re-run extraction merely because candidates were requested and found absent; re-running SHALL be an explicit action. This requirement SHALL NOT apply to a capture that has no purchase yet: its candidates are never held server-side at all, transiently or otherwise.

#### Scenario: Candidates do not outlive a restart

- **WHEN** a purchase's receipt has unconfirmed candidates from a re-run and the system is restarted
- **THEN** the candidates are no longer available
- **AND** the extraction state, failure reason and fiscal identifiers recorded for that image are unchanged

#### Scenario: Confirmed lines are unaffected by a restart

- **WHEN** candidates were confirmed into expenses and the system is restarted
- **THEN** those expenses are unchanged
- **AND** the purchase still reconciles

#### Scenario: Requesting candidates that are no longer held

- **WHEN** the candidates of a purchase's receipt whose recorded state is Extracted are requested after a restart and none are held
- **THEN** the response reports that no candidates are available, together with the recorded state
- **AND** it is not reported as an extraction failure
- **AND** extraction is not started

#### Scenario: Re-running restores candidates

- **WHEN** extraction is re-run for a purchase's receipt whose candidates are no longer held
- **THEN** candidates are produced again, synchronously, in the response
- **AND** any expenses already confirmed for that purchase are unchanged

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

### Requirement: Vision extraction is pluggable

The vision extraction stage SHALL sit behind a boundary so that its engine can be replaced without changing how purchases, images, candidates, validation or the cascade behave. The vision stage SHALL be the fallback for receipts the deterministic path cannot serve, and SHALL NOT run for a receipt whose invoice was retrieved and validated. The stage SHALL analyze the receipt image itself and produce candidate lines, a total, and a merchant name derived from what the image shows, rather than from any property of the image file unrelated to its content. Every result SHALL record the engine name and version that produced it, so results from different engines, or from a fixture used in tests, stay identifiable from one another. The system SHALL make clear, wherever candidates are surfaced, which engine produced them.

#### Scenario: The engine reads the image content

- **WHEN** two different receipt images are extracted by the vision stage
- **THEN** the candidate lines reported for each reflect what that image shows
- **AND** two different images do not produce the same candidate lines merely because nothing about the image's content was examined

#### Scenario: Results are identified by engine

- **WHEN** candidate lines produced by the vision stage are retrieved
- **THEN** the response identifies the engine name and version that produced them

#### Scenario: An unverifiable value carries the engine's own confidence

- **WHEN** the vision stage extracts a description, merchant name, or category or unit guess
- **THEN** the value carries the confidence the engine itself reported for it
- **AND** a value arithmetic can verify carries no reported confidence

#### Scenario: The vision stage does not run behind a retrieved invoice

- **WHEN** a receipt's invoice is retrieved from the verification service and passes validation
- **THEN** no vision-stage candidates are produced for that receipt

### Requirement: Extraction can be re-run

A user SHALL be able to re-run extraction for a purchase that carries a receipt image or a fiscal identity. Re-running SHALL run synchronously and SHALL replace any existing unconfirmed candidates for that purchase with the outcome of the new run, and SHALL NOT alter expenses that have already been confirmed. Where the purchase carries a fiscal identity and no image, the re-run SHALL run only the steps that work from a fiscal identity.

#### Scenario: Re-run replaces candidates

- **WHEN** extraction is re-run for a purchase that has unconfirmed candidates
- **THEN** the previous candidates are replaced by the new ones, synchronously, in the response

#### Scenario: Re-run after confirmation

- **WHEN** extraction is re-run for a purchase whose candidates were already confirmed into expenses
- **THEN** the expenses of the purchase are unchanged
- **AND** the new candidates are offered separately

#### Scenario: Re-run a failed extraction

- **WHEN** extraction is re-run for a purchase in the Failed state
- **THEN** it is extracted again, synchronously, and reaches a new terminal state in the same response

#### Scenario: Re-run with no image

- **WHEN** extraction is re-run for a purchase that carries a fiscal identity and no receipt image
- **THEN** the invoice is sought from the verification service using the retained payload
- **AND** no step that reads an image runs

### Requirement: Stored images can be retrieved

The system SHALL allow retrieving the stored bytes of a receipt image attached to a purchase, together with its content type, so that a client can display it beside the expense lines.

#### Scenario: Retrieve an image

- **WHEN** the receipt image of a purchase is requested
- **THEN** the original bytes are returned with the correct content type

#### Scenario: Retrieve an image that does not exist

- **WHEN** the receipt image of a purchase that has none is requested
- **THEN** the system reports that it was not found

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

### Requirement: Fiscal receipt identity is captured when present

Where a receipt carries fiscal identifiers issued by a tax authority, the system SHALL retain them
as the purchase's fiscal identity exactly as read, without imposing a format. A fiscal QR payload SHALL
be accepted alongside an image capture, and on its own with no image, as well as decoded from an
image, so that a client which read the code need not depend on the server rediscovering it. Where a
payload is supplied, it SHALL be preferred outright and the image SHALL NOT be decoded for a second
reading, because a client reading a live camera has retries and focus available to it that a single
stored frame does not. Each identifier SHALL be taken only from a source that actually carries it:
an identifier absent from the fiscal code SHALL NOT be populated from another value found there.

#### Scenario: Identifiers read from the receipt

- **WHEN** extraction reads fiscal identifiers from a captured image
- **THEN** they are returned in the capture response, and retained as the fiscal identity of the purchase the capture is confirmed into

#### Scenario: A payload supplied with the capture

- **WHEN** a receipt image is captured together with a fiscal QR payload
- **THEN** the identifiers it carries are retained with the capture without extraction needing to rediscover them
- **AND** the image is not decoded for a second reading

#### Scenario: A payload captured on its own

- **WHEN** a fiscal QR payload is captured with no image
- **THEN** the identifiers it carries are reported in the capture response
- **AND** they are recorded as supplied by the client

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

### Requirement: An image can be captured with no purchase behind it

The system SHALL accept a receipt image with no reference to any purchase. Capture SHALL store the image in a temporary area, keyed by a system-generated identifier distinct from any purchase or content identifier, and SHALL run extraction against it before responding. Capture SHALL NOT create a purchase, and SHALL NOT require one to already exist.

#### Scenario: Capturing an image with nothing else known

- **WHEN** a receipt image is captured with no purchase, date, amount or expense lines supplied
- **THEN** the image is stored temporarily and extracted
- **AND** no purchase is created
- **AND** the response identifies the temporary image by its system-generated key

#### Scenario: A temporary key is never reused

- **WHEN** two receipt images are captured, whether or not their bytes are identical
- **THEN** each capture is given a key of its own

### Requirement: A captured image is held temporarily until confirmed

Bytes accepted by capture SHALL be written to a temporary store distinct from the permanent receipt store, addressed only by the key capture returned. Nothing in the temporary store SHALL be treated as part of the ledger, retrievable as a purchase's receipt, or reachable by any means other than the confirm operation that names its key.

#### Scenario: A temporary capture is not a purchase's receipt

- **WHEN** an image has been captured but not yet confirmed
- **THEN** no purchase exists that carries it
- **AND** it cannot be retrieved as any purchase's receipt image

#### Scenario: Confirming promotes the temporary image

- **WHEN** a capture is confirmed
- **THEN** its bytes are moved into the permanent receipt store under a content-addressed reference
- **AND** the temporary copy no longer exists

#### Scenario: Confirming an unknown or expired key

- **WHEN** confirmation names a temporary key that does not exist
- **THEN** the system rejects the confirmation and reports that the capture was not found
- **AND** no purchase is created

### Requirement: A capture's candidates are held by the caller, not the server

Extraction results produced by capture SHALL be returned in the capture response and SHALL NOT be retained by the system against the temporary key. Confirming a capture SHALL require the caller to submit the purchase's date, amount and expense lines explicitly; the system SHALL NOT reconstruct them from a capture result it no longer holds.

#### Scenario: Nothing is held between capture and confirm

- **WHEN** a capture completes and no confirmation follows
- **THEN** the system holds no candidate lines, amount, merchant or date against that capture's key
- **AND** only the temporary image file remains, subject to the orphan cleanup rule

#### Scenario: Confirm carries its own data

- **WHEN** a capture is confirmed
- **THEN** the date, amount and expense lines used to create the purchase are exactly those submitted with the confirmation
- **AND** they need not match what the capture response reported, since the caller may have edited them

### Requirement: Confirming a capture creates the purchase

Confirming a capture SHALL create a purchase in one step: the submitted date, amount and expense lines SHALL reconcile exactly as they would for a manually recorded purchase, and the promoted image SHALL be attached as that purchase's receipt from the moment it is created, already in a terminal extraction state. No purchase SHALL exist part-way through confirmation: either the purchase is created whole, with its receipt attached, or nothing is created at all.

#### Scenario: A confirmed capture becomes a whole purchase

- **WHEN** a capture is confirmed with a date, an amount and expense lines that reconcile
- **THEN** a purchase is created with that date and amount
- **AND** the promoted image is attached as its receipt
- **AND** the receipt's extraction state is already terminal, with no further extraction required

#### Scenario: Confirmation that does not reconcile

- **WHEN** a capture is confirmed with expense lines whose amounts do not sum to the submitted purchase amount
- **THEN** the system rejects the confirmation and reports the discrepancy
- **AND** no purchase is created
- **AND** the temporary image is left in place, available to confirm again

### Requirement: The purchase date defaults from the receipt

Where confirmation does not supply a date, and the capture's extraction established the invoice creation timestamp from the receipt's fiscal identity, the system SHALL use that timestamp as the purchase's occurrence. Where no date is supplied and none was established from the receipt, the system SHALL reject the confirmation and report that a date is required.

#### Scenario: Date taken from a decoded fiscal receipt

- **WHEN** a capture's fiscal QR decodes an invoice creation timestamp and confirmation supplies no date
- **THEN** the purchase's occurrence is that timestamp

#### Scenario: Caller's date overrides the receipt

- **WHEN** confirmation supplies a date that differs from the timestamp the receipt carried
- **THEN** the purchase's occurrence is the date confirmation supplied

#### Scenario: No date anywhere

- **WHEN** confirmation supplies no date and the receipt carried no fiscal timestamp
- **THEN** the system rejects the confirmation and reports that a date is required
- **AND** no purchase is created

### Requirement: Orphaned captures are removed automatically

The system SHALL remove, on a recurring daily schedule, any temporary capture that has not been confirmed within one day of being captured. Removal SHALL be independent of any request and SHALL NOT report an error for a capture that no longer exists by the time confirmation is attempted against it.

#### Scenario: An old, unconfirmed capture is swept away

- **WHEN** the daily cleanup runs and a temporary capture is older than one day and was never confirmed
- **THEN** its temporary file is deleted

#### Scenario: A recent capture survives a cleanup run

- **WHEN** the daily cleanup runs and a temporary capture is less than one day old
- **THEN** the capture is left in place

#### Scenario: Confirming after cleanup already removed it

- **WHEN** confirmation names a key for a capture the daily cleanup already removed
- **THEN** the system reports that the capture was not found, the same as for any unknown key
- **AND** the cleanup itself is not treated as having failed

### Requirement: The fiscal QR payload is retained with the receipt

Where a fiscal QR payload is obtained for a purchase — supplied by the client at capture, with or
without an image, or decoded from the image — the system SHALL retain the payload verbatim as part of
the purchase's fiscal identity, and SHALL retain it whether or not any identifier could be parsed out
of it. Where a payload is retained, a later extraction for the same purchase SHALL use it rather than
attempting to decode the image again. A purchase for which no payload was obtained SHALL record its
absence rather than an empty payload.

#### Scenario: A supplied payload is retained

- **WHEN** a receipt is captured with a fiscal QR payload and the capture is confirmed
- **THEN** the payload is retained with the purchase exactly as it was submitted

#### Scenario: A payload captured without an image is retained

- **WHEN** a fiscal-only capture is confirmed
- **THEN** its payload is retained with the purchase exactly as it was submitted

#### Scenario: A decoded payload is retained

- **WHEN** no payload was supplied and one is decoded from the image
- **THEN** the decoded payload is retained with the purchase

#### Scenario: A re-run does not decode the image again

- **WHEN** extraction is re-run for a purchase that retained a fiscal QR payload
- **THEN** the retained payload is used
- **AND** no attempt is made to decode the image

#### Scenario: An unrecognised payload is still retained

- **WHEN** a payload is obtained whose format yields no recognisable identifiers
- **THEN** the payload is still retained with the purchase

#### Scenario: A receipt with no payload

- **WHEN** a receipt is confirmed for which no payload was supplied or decoded
- **THEN** the purchase records that no payload is held

### Requirement: Vision extraction failure is an ordinary outcome

The vision stage SHALL treat every failure to produce a result — the engine being unreachable, answering too slowly, refusing the request, or answering with something the stage cannot interpret as the expected shape — as the stage producing nothing, the same as any other optional extraction stage. A missing or invalid engine credential SHALL also be treated this way rather than raised during startup or as an unhandled error, since the vision stage may be configured with no credential at all where it is not needed. Because the vision stage is the last stage in the cascade, a failure here with no deterministic result already established SHALL leave the receipt without candidates rather than substituting a fabricated or estimated one.

#### Scenario: The engine cannot be reached

- **WHEN** the vision engine cannot be reached or does not answer within its time budget
- **THEN** the outcome is the same as a stage that produced nothing
- **AND** no error about the receipt is reported to the user

#### Scenario: The engine's answer cannot be interpreted

- **WHEN** the vision engine answers with a response that does not carry the fields extraction expects
- **THEN** the outcome is the same as a stage that produced nothing

#### Scenario: No credential is configured

- **WHEN** the vision stage is asked to run with no engine credential configured
- **THEN** the stage produces nothing
- **AND** the system does not fail to start on that account

#### Scenario: A vision failure with nothing deterministic behind it

- **WHEN** no deterministic stage produced a result for a receipt and the vision stage then fails
- **THEN** the receipt's extraction reaches the Failed state
- **AND** the temporary or attached image remains available so the caller can confirm it with lines entered by hand

### Requirement: Fiscal identity belongs to the purchase, not to the image

A purchase SHALL be able to carry a fiscal identity — the fiscal QR payload, the identifiers read
from it or answered for it, and how each was established — independently of whether it carries a
receipt image. A purchase SHALL carry a fiscal identity, a receipt image, both, or neither. Removing a
purchase's receipt image SHALL NOT remove its fiscal identity, and a purchase carrying a fiscal
identity and no image SHALL be treated as having a receipt for every purpose except retrieving
image bytes.

#### Scenario: A purchase with a fiscal identity and no image

- **WHEN** a purchase is created from a capture that carried a fiscal QR payload and no image
- **THEN** the purchase carries that fiscal identity
- **AND** it carries no receipt image

#### Scenario: A purchase with both

- **WHEN** a purchase is created from an image capture whose fiscal QR payload was supplied or decoded
- **THEN** the purchase carries both the receipt image and the fiscal identity

#### Scenario: Deleting the image keeps the fiscal identity

- **WHEN** the receipt image of a purchase that also carries a fiscal identity is deleted
- **THEN** the image is removed under the existing sharing rule
- **AND** the purchase's fiscal identity and extraction state are unchanged

#### Scenario: Existing receipts keep their fiscal identity

- **WHEN** a purchase recorded before this change carried fiscal identifiers or a payload with its receipt image
- **THEN** after the change it carries the same identifiers, payload and sources as its fiscal identity
- **AND** its receipt image is unchanged

### Requirement: A fiscal QR payload can be captured with no image

The system SHALL accept a fiscal QR payload on its own, with no image and no purchase referenced, and
SHALL run extraction against it before responding. Extraction of such a capture SHALL run only the
steps that work from a fiscal identity — parsing the payload and retrieving the invoice from the
verification service — and SHALL NOT run any step that reads an image. Capture SHALL NOT create a
purchase, SHALL NOT store anything, and SHALL NOT return a temporary key, since there are no bytes
to hold. Where no invoice is retrieved, the outcome SHALL be Failed, and the identifiers parsed from
the payload SHALL still be reported, so that the caller can decide whether to supply the payload
again alongside a photograph.

#### Scenario: A payload that yields an invoice

- **WHEN** a fiscal QR payload is captured on its own and the verification service returns its invoice
- **THEN** the outcome is Extracted or NeedsReview, as validation decides
- **AND** the candidate lines are the invoice's lines
- **AND** the response carries no temporary key

#### Scenario: The service returns no invoice

- **WHEN** a fiscal QR payload is captured on its own and the verification service has no record of it, cannot be reached, or answers in an unrecognised shape
- **THEN** the outcome is Failed with the reason reported
- **AND** the identifiers parsed from the payload are reported
- **AND** no step that reads an image runs

#### Scenario: A payload no identifier can be read from

- **WHEN** a payload that yields no recognisable identifiers is captured on its own
- **THEN** the outcome is Failed
- **AND** the response reports no fiscal identifiers
- **AND** the verification service is not asked

#### Scenario: Nothing is held after a fiscal capture

- **WHEN** a fiscal capture completes and no confirmation follows
- **THEN** the system holds nothing about it

### Requirement: A fiscal-only capture is confirmed by its payload

Confirming a capture that carried no image SHALL create a purchase in one step, from the submitted
date, amount and expense lines together with the payload, the issuer verification code and the
fiscal source the capture response reported. Confirmation SHALL reconcile exactly as it does for an
image capture and SHALL apply the same date default. The created purchase SHALL carry the fiscal
identity and an extraction state already terminal, and SHALL carry no receipt image. A confirmation
SHALL identify either a temporary image or a payload, and SHALL be rejected if it identifies neither.

#### Scenario: A confirmed fiscal capture becomes a whole purchase

- **WHEN** a fiscal-only capture is confirmed with its payload and a date, amount and lines that reconcile
- **THEN** a purchase is created with that date and amount
- **AND** it carries the fiscal identity parsed from the payload, with the issuer verification code and source submitted
- **AND** its extraction state is already terminal
- **AND** it carries no receipt image

#### Scenario: The date defaults from the payload

- **WHEN** a fiscal-only capture whose payload states the invoice creation timestamp is confirmed with no date
- **THEN** the purchase's occurrence is that timestamp

#### Scenario: A confirmation naming neither an image nor a payload

- **WHEN** a capture confirmation supplies neither a temporary key nor a fiscal QR payload
- **THEN** the system rejects the confirmation
- **AND** no purchase is created

### Requirement: A capture reports an invoice already recorded

Where a capture — of an image or of a payload alone — establishes an invoice identification code
that a recorded purchase already carries, the capture response SHALL identify that purchase and when
it occurred. The report SHALL be a warning only: it SHALL NOT change the extraction outcome and
SHALL NOT prevent the capture from being confirmed. Where the code is carried by more than one
recorded purchase, the most recent SHALL be reported.

#### Scenario: A receipt scanned twice

- **WHEN** a payload whose invoice identification code is already carried by a recorded purchase is captured
- **THEN** the capture response identifies that purchase and its occurrence
- **AND** the extraction outcome is the same as it would be otherwise

#### Scenario: A duplicate can still be confirmed

- **WHEN** a capture reported as an already-recorded invoice is confirmed
- **THEN** the purchase is created as for any other capture

#### Scenario: A new invoice

- **WHEN** a capture establishes an invoice identification code no recorded purchase carries
- **THEN** the capture response reports no earlier purchase

#### Scenario: No invoice identification code

- **WHEN** a capture establishes no invoice identification code
- **THEN** the capture response reports no earlier purchase

