## ADDED Requirements

### Requirement: A capture response carries its fiscal diagnosis

Both HTTP capture endpoints SHALL report the capture's fiscal diagnosis in their response. It SHALL
have a kind from a closed, documented set, a short human-readable detail, and the specifics the kind
calls for. It SHALL be an addition to the response shape: no existing field SHALL change meaning,
and a successful response SHALL stay successful whatever the diagnosis.

#### Scenario: Reading why a fiscal capture fetched nothing

- **WHEN** a fiscal payload is captured and the verification service cannot be reached
- **THEN** the response succeeds with outcome Failed
- **AND** it carries a fiscal diagnosis whose kind states that the service was unreachable

#### Scenario: Reading missing parameters

- **WHEN** a payload carrying an invoice code but no creation timestamp is captured
- **THEN** the fiscal diagnosis names the creation timestamp as missing

#### Scenario: Reading a refusal

- **WHEN** the verification service answers a capture's request with status 500
- **THEN** the fiscal diagnosis carries status 500

#### Scenario: Reading an unreadable answer

- **WHEN** the verification service answers a capture's request with a body that is not a readable invoice
- **THEN** the fiscal diagnosis carries an excerpt of that body no longer than 200 characters, never the whole of it

#### Scenario: A retrieved invoice

- **WHEN** a capture's invoice is retrieved from the verification service
- **THEN** the fiscal diagnosis kind states that it was retrieved
