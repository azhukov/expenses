## ADDED Requirements

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

## MODIFIED Requirements

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
