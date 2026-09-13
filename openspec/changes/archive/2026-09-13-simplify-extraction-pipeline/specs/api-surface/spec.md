## MODIFIED Requirements

### Requirement: Extraction results are legible over both interfaces

Both interfaces SHALL report, for an extraction, the steps that ran, the step that produced each
value, the outcome of each arithmetic check, and any fiscal identifiers with how they were
established. A result produced by a placeholder engine SHALL be identifiable as such wherever it is
surfaced. Because candidates are not persisted, both interfaces SHALL be able to report a purchase
whose candidates are no longer held: the recorded extraction state SHALL still be returned, the
absence of candidates SHALL be distinguishable from an extraction that produced none, and neither
interface SHALL start extraction in response to a read.

#### Scenario: Reading a validated extraction

- **WHEN** an extraction result is retrieved over either interface
- **THEN** it reports each arithmetic check and whether it passed, failed, or did not apply

#### Scenario: Reading a failed check

- **WHEN** an extraction whose lines do not sum to its total is retrieved
- **THEN** the response names the extracted total and the computed sum

#### Scenario: Reading how fiscal identity was established

- **WHEN** an extraction whose fiscal identifiers were supplied at capture is retrieved
- **THEN** the response states that they were supplied rather than decoded from the image or answered by the verification service

#### Scenario: Placeholder results stay identifiable

- **WHEN** candidates produced by a placeholder step are retrieved over either interface
- **THEN** both identify the engine and step that produced them

#### Scenario: Reading an image whose candidates are no longer held

- **WHEN** the extraction of a purchase whose candidates were discarded on restart is read over either interface
- **THEN** both report the recorded extraction state and that no candidates are available
- **AND** neither starts extraction
- **AND** the response distinguishes this from an extraction that produced no lines

### Requirement: Fiscal identifiers may accompany a capture

The HTTP interface SHALL accept a fiscal QR payload, as the raw string the code carries, alongside a
multipart capture, so that a client which read the code at capture time can supply it without
waiting for extraction. The payload SHALL be parsed server-side rather than requiring the client to
parse it, so that one implementation reads the format. The interface SHALL bound the accepted
payload length, and SHALL treat an unparseable payload as an ordinary capture carrying no
identifiers rather than as a bad request. The interface SHALL NOT accept pre-parsed fiscal
identifiers. The MCP interface SHALL be able to read the identifiers and how they were established,
but SHALL NOT accept image bytes.

#### Scenario: Capture carrying a fiscal QR payload

- **WHEN** a receipt image is captured together with a fiscal QR payload
- **THEN** the temporary capture carries the identifiers parsed from that payload
- **AND** they are readable in the capture response before confirmation

#### Scenario: Capture carrying an unparseable payload

- **WHEN** a receipt image is captured together with a payload no identifier can be read from
- **THEN** the capture succeeds
- **AND** the response reports no fiscal identifiers

#### Scenario: Capture carrying an oversized payload

- **WHEN** a capture supplies a payload longer than the accepted bound
- **THEN** the request is rejected before the payload is parsed

#### Scenario: Assistant reads how identity was established

- **WHEN** an assistant asks over MCP about the receipt of a purchase whose payload was supplied at capture
- **THEN** the response states that the identifiers were supplied rather than decoded or retrieved
