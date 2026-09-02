## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: A purchase carries at most one receipt image

A purchase SHALL have zero or one receipt image. A purchase created by manual entry, with no capture behind it, SHALL have none. A receipt SHALL belong to exactly one purchase and SHALL have no identity of its own: every operation on a receipt — retrieving its bytes, reading its extraction state, re-running extraction — SHALL address it by the purchase that carries it. An image SHALL NOT exist attached to a purchase other than through confirming a capture: a purchase acquires its receipt, if any, at the moment it is created, and never afterward.

#### Scenario: A receipt is addressed through its purchase

- **WHEN** the receipt of a purchase is retrieved, its extraction state read, or its extraction re-run
- **THEN** the purchase identifies it in every case
- **AND** no separate image identifier is required or exposed

#### Scenario: Manual purchase has no image

- **WHEN** a purchase is recorded by manual entry with no capture confirmed alongside it
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

Every attached receipt image SHALL have an extraction state, recorded against the purchase that carries it, and that state SHALL be observable. The states SHALL be Extracted, NeedsReview and Failed. Extraction SHALL run to completion within the request that triggers it — capturing an image, or re-running extraction for a purchase's receipt — and no Pending or Extracting state SHALL be observable between requests: by the time a capture response or a re-run response is returned, extraction has already reached one of the three terminal states.

#### Scenario: State on capture

- **WHEN** a receipt image is captured
- **THEN** the response does not return until extraction has completed
- **AND** it reports one of Extracted, NeedsReview or Failed

#### Scenario: Successful extraction

- **WHEN** extraction of a captured image completes, its result passes arithmetic validation, and no unverifiable value falls below the confidence threshold
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

- **WHEN** extraction cannot produce any result
- **THEN** the outcome is Failed
- **AND** the reason for failure is available
- **AND** the temporary image remains available so the caller can confirm it with lines entered by hand

#### Scenario: Extraction state is readable

- **WHEN** a purchase with a receipt image is retrieved
- **THEN** the extraction state of its image is included, and is one of Extracted, NeedsReview or Failed

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

### Requirement: Extraction can be re-run

A user SHALL be able to re-run extraction for a receipt image already attached to a purchase. Re-running SHALL run synchronously and SHALL replace any existing unconfirmed candidates for that image with the outcome of the new run, and SHALL NOT alter expenses that have already been confirmed.

#### Scenario: Re-run replaces candidates

- **WHEN** extraction is re-run for an image that has unconfirmed candidates
- **THEN** the previous candidates are replaced by the new ones, synchronously, in the response

#### Scenario: Re-run after confirmation

- **WHEN** extraction is re-run for an image whose candidates were already confirmed into expenses
- **THEN** the expenses of the purchase are unchanged
- **AND** the new candidates are offered separately

#### Scenario: Re-run a failed extraction

- **WHEN** extraction is re-run for an image in the Failed state
- **THEN** it is extracted again, synchronously, and reaches a new terminal state in the same response

### Requirement: Fiscal receipt identity is captured when present

Where a receipt carries fiscal identifiers issued by a tax authority, the system SHALL retain them alongside the image exactly as read, without imposing a format. Fiscal identifiers SHALL be accepted alongside a capture as well as discovered from the image, so that a client which decodes them at capture time need not depend on the server rediscovering them. Each identifier SHALL be taken only from a source that actually carries it: an identifier absent from the fiscal code SHALL NOT be populated from another value found there. Where an identifier is known from more than one source, the system SHALL compare them and SHALL report a disagreement rather than silently preferring one.

#### Scenario: Identifiers read from the receipt

- **WHEN** extraction reads fiscal identifiers from a captured image
- **THEN** they are retained with the image and returned in the capture response, and again when the confirmed purchase's receipt is retrieved

#### Scenario: Identifiers supplied with the capture

- **WHEN** a receipt image is captured together with fiscal identifiers decoded by the client
- **THEN** they are retained with the capture without extraction needing to rediscover them

#### Scenario: Sources agree

- **WHEN** a fiscal identifier supplied at capture matches the one extraction reads from the same image
- **THEN** the identifier is reported as corroborated

#### Scenario: Sources disagree

- **WHEN** a fiscal identifier supplied at capture differs from the one extraction reads from the same image
- **THEN** both values are retained
- **AND** the disagreement is reported
- **AND** the outcome is NeedsReview

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
