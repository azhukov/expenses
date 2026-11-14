## MODIFIED Requirements

### Requirement: Invoice detail is retrieved from the fiscal verification portal

Where a fiscal QR payload has been obtained, the system SHALL retrieve the corresponding invoice
from the fiscal verification service the code refers to, and SHALL map the returned invoice onto
extraction candidates. All receipts are assumed to be issued under the same national fiscalisation
scheme and verified by the same service. A retrieved invoice SHALL be treated as authoritative: its
line items, quantities, units, tax rates, seller identity and totals SHALL be used verbatim rather
than re-estimated, and SHALL NOT be re-derived by any later step, whether or not it reconciles
arithmetically. Where no invoice is obtained, extraction SHALL continue exactly as for a step that
produced nothing, and no error SHALL be raised; the reason no invoice was obtained SHALL nevertheless
be classified and reported as the capture's fiscal diagnosis.

#### Scenario: Invoice detail is retrieved and mapped

- **WHEN** an invoice is retrieved for a fiscal code
- **THEN** each returned line becomes a candidate carrying its description, quantity, unit, tax rate and amount
- **AND** the seller name and tax identification number are recorded as the merchant
- **AND** the candidates identify the retrieval step as their source
- **AND** the fiscal diagnosis reports that the invoice was retrieved

#### Scenario: A retrieved invoice that does not reconcile is still authoritative

- **WHEN** an invoice retrieved from the verification service fails an arithmetic check
- **THEN** it is retained as the result
- **AND** no probabilistic step is asked to produce an alternative
- **AND** the extraction state is NeedsReview

#### Scenario: The service has no record of the invoice

- **WHEN** the verification service answers successfully with no invoice for a fiscal code
- **THEN** extraction continues with the remaining step
- **AND** the identifiers read from the code are still retained
- **AND** the fiscal diagnosis reports that the service has no record

#### Scenario: The service cannot be reached

- **WHEN** the verification service cannot be reached or does not answer within its time budget
- **THEN** the outcome is the same as a step that produced nothing
- **AND** no error about the receipt is raised
- **AND** the values already read from the fiscal QR are retained
- **AND** the fiscal diagnosis reports that the service was unreachable

#### Scenario: The service refuses the request

- **WHEN** the verification service answers with a non-success status
- **THEN** the outcome is the same as a step that produced nothing
- **AND** the fiscal diagnosis reports that the service refused the request, with the status it answered

#### Scenario: The service answers in an unreadable shape

- **WHEN** the verification service answers successfully with a body that is not a readable invoice, or an invoice with no lines
- **THEN** the outcome is the same as a step that produced nothing
- **AND** the fiscal diagnosis reports that the answer could not be read, with an excerpt of the answer no longer than 200 characters

#### Scenario: Retrieval is not repeated needlessly

- **WHEN** extraction is re-run for a receipt whose invoice was already retrieved
- **THEN** the retrieved invoice is reused rather than requested again
- **AND** the fiscal diagnosis reports that the invoice was retrieved

## ADDED Requirements

### Requirement: Why no invoice was obtained is diagnosed

Every capture SHALL carry a fiscal diagnosis: exactly one outcome of the attempt to obtain the
invoice through the receipt's fiscal code. The possible outcomes are no payload, not a fiscal code,
incomplete parameters, service unreachable, refused, no record, unreadable answer, or retrieved. It
SHALL be informational only, SHALL NOT be persisted, and SHALL NOT change the steps run, the state, or
whether the capture can be confirmed.

#### Scenario: No payload at all

- **WHEN** an image is captured with no payload, and no fiscal code can be decoded from it
- **THEN** the fiscal diagnosis reports that no payload was obtained

#### Scenario: A code that is not a fiscal code

- **WHEN** a payload carrying none of the invoice code, issuer tax number or creation timestamp is captured
- **THEN** the fiscal diagnosis reports that the payload is not a fiscal code
- **AND** the verification service is not asked

#### Scenario: A fiscal code missing parameters

- **WHEN** a payload carrying some but not all of the invoice code, issuer tax number and creation timestamp is captured
- **THEN** the fiscal diagnosis reports that the identifiers are incomplete and names each one missing
- **AND** the verification service is not asked

#### Scenario: Diagnosis does not change the outcome

- **WHEN** the same capture is run with any fiscal diagnosis
- **THEN** its extraction state and candidates are those the cascade would have produced without the diagnosis

#### Scenario: A verification-service failure is logged

- **WHEN** the verification service is unreachable, refuses, has no record, or answers unreadably
- **THEN** the diagnosis is written to the durable log at warning level or above
