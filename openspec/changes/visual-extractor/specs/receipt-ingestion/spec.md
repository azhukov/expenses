## MODIFIED Requirements

### Requirement: Vision extraction is pluggable and mocked in this change

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

## ADDED Requirements

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
