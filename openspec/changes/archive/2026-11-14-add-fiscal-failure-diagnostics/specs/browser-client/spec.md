## MODIFIED Requirements

### Requirement: A photograph is taken only when the code produces no invoice

Where the fiscal-capture endpoint returns Extracted or NeedsReview, the client SHALL present the
result for review and SHALL NOT ask for a photograph. Where it returns Failed, the client SHALL state
why the invoice could not be fetched, as the response's fiscal diagnosis reports it, and SHALL present
the photograph control. Where it cannot be reached, the client SHALL say so. Where the diagnosis
concerns the payload itself (not a fiscal code, or missing verification parameters), the client SHALL
also show the text that was read, so that the user can tell which code was scanned. Where the
diagnosis concerns missing parameters, the client SHALL name them. Where a Failed fiscal capture
reported fiscal identifiers, the photograph SHALL be uploaded together with the payload that was read.
Where it reported none, the photograph SHALL be uploaded without it, so that an unrecognised code does
not stop the ledger reading the photograph itself. A response carrying no diagnosis SHALL be reported
with the general statement that the invoice could not be fetched.

#### Scenario: The invoice is fetched

- **WHEN** a fiscal capture returns Extracted or NeedsReview
- **THEN** the candidates are presented for review
- **AND** no photograph is asked for

#### Scenario: The invoice is not fetched

- **WHEN** a fiscal capture returns Failed having reported fiscal identifiers, with a diagnosis that the verification service was unreachable
- **THEN** the screen states that the fiscal service could not be reached and presents the photograph control
- **AND** the photograph taken is uploaded together with the payload that was read

#### Scenario: The verification service refused or answered unreadably

- **WHEN** a fiscal capture returns Failed with a diagnosis that the service refused the request, has no record, or answered in a shape that could not be read
- **THEN** the screen states that specific reason, including the status for a refusal
- **AND** presents the photograph control

#### Scenario: The code was not a fiscal code

- **WHEN** a fiscal capture returns Failed with a diagnosis that the payload is not a fiscal code
- **THEN** the screen states that the scanned code is not a fiscal receipt code and shows the text that was read
- **AND** the photograph control is presented
- **AND** the photograph taken is uploaded without the payload

#### Scenario: The fiscal code was incomplete

- **WHEN** a fiscal capture returns Failed with a diagnosis that verification parameters are missing
- **THEN** the screen names the missing parameters and shows the text that was read
- **AND** the photograph control is presented

#### Scenario: The fiscal capture cannot be reached

- **WHEN** the fiscal-capture request fails before a response is received
- **THEN** the screen reports that the ledger could not be reached
- **AND** offers to retry the same payload
- **AND** offers the photograph control
