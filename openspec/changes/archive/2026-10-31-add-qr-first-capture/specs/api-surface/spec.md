## ADDED Requirements

### Requirement: A fiscal QR payload can be captured over HTTP without an image

The HTTP interface SHALL accept a fiscal QR payload on its own, as the raw string the code carries,
at an endpoint distinct from image capture, and SHALL respond with the extraction outcome in the
same shape as an image capture, carrying no temporary key. The payload SHALL be parsed server-side
and bounded in length exactly as a payload accompanying an image capture is. An unparseable payload
SHALL be an ordinary capture whose outcome is Failed rather than a bad request; a missing or empty
payload SHALL be a bad request.

#### Scenario: Capturing a payload

- **WHEN** a fiscal QR payload is submitted to the fiscal-capture endpoint
- **THEN** the response reports the extraction outcome, candidates and fiscal identifiers in the same shape as an image capture
- **AND** it carries no temporary key

#### Scenario: An oversized payload

- **WHEN** a payload longer than the accepted bound is submitted to the fiscal-capture endpoint
- **THEN** the request is rejected before the payload is parsed

#### Scenario: A missing payload

- **WHEN** the fiscal-capture endpoint is called with no payload or an empty one
- **THEN** the request is rejected as a validation failure naming the payload

#### Scenario: An unparseable payload

- **WHEN** a payload no identifier can be read from is submitted to the fiscal-capture endpoint
- **THEN** the request succeeds
- **AND** the outcome is Failed with no fiscal identifiers reported

### Requirement: A capture response names an invoice already recorded

Both capture endpoints SHALL report, where the capture established an invoice identification code
that a recorded purchase already carries, the identifier and occurrence of that purchase, and SHALL
report nothing in its place otherwise.

#### Scenario: Reading a duplicate warning

- **WHEN** a capture establishes an invoice identification code already carried by a recorded purchase
- **THEN** the response carries that purchase's identifier and occurrence

#### Scenario: No duplicate

- **WHEN** a capture establishes no invoice identification code, or one no recorded purchase carries
- **THEN** the response reports no earlier purchase

### Requirement: Fiscal identity is reported apart from the receipt image

Both interfaces SHALL report a purchase's fiscal identity and its receipt image as separate things,
so that a purchase carrying one without the other is legible as such. A purchase's read shape SHALL
state whether it carries a receipt image and whether it carries a fiscal identity, and SHALL report
its extraction state whenever it carries either.

#### Scenario: A purchase with a fiscal identity and no image

- **WHEN** a purchase created from a fiscal-only capture is retrieved over either interface
- **THEN** it reports its fiscal identity and extraction state
- **AND** it reports that it carries no receipt image

#### Scenario: Requesting the image of an image-less purchase

- **WHEN** the receipt image bytes of a purchase carrying only a fiscal identity are requested
- **THEN** the system reports that it was not found

## MODIFIED Requirements

### Requirement: Receipt capture is HTTP-only

Receipt capture SHALL be accepted over HTTP only: an image as a multipart form submission carrying the raw file, and a fiscal QR payload on its own at the fiscal-capture endpoint, each with no purchase referenced. The MCP interface SHALL NOT accept image bytes as tool arguments, for capture or for any other operation, and SHALL NOT offer fiscal capture. MCP SHALL instead be able to confirm a capture by its temporary key or by its fiscal QR payload, to reference receipts already attached to a purchase, and to trigger and read extraction for them. Neither interface SHALL expose an identifier for a receipt image once it is attached to a purchase: a receipt is addressed by its purchase from that point on.

#### Scenario: Capture over HTTP

- **WHEN** a receipt image is submitted as a multipart form upload with no purchase referenced
- **THEN** the image is stored temporarily and extracted
- **AND** the response identifies it by its temporary key rather than by a purchase or image identifier

#### Scenario: Fiscal capture over HTTP

- **WHEN** a fiscal QR payload is submitted on its own with no purchase referenced
- **THEN** it is extracted and nothing is stored
- **AND** the response carries no temporary key

#### Scenario: MCP confirms a capture

- **WHEN** an assistant asks over MCP to confirm a capture by its temporary key, supplying the purchase's date, amount and lines
- **THEN** the purchase is created with its receipt attached

#### Scenario: MCP confirms a fiscal-only capture

- **WHEN** an assistant asks over MCP to confirm a capture by its fiscal QR payload, supplying the purchase's date, amount and lines
- **THEN** the purchase is created carrying that fiscal identity and no receipt image

#### Scenario: MCP references a stored receipt

- **WHEN** an assistant asks over MCP for the extraction state of the receipt of a purchase
- **THEN** the state and any candidate lines are returned

#### Scenario: MCP triggers extraction

- **WHEN** an assistant asks over MCP to re-run extraction for the receipt of a purchase
- **THEN** extraction runs synchronously and the resulting state is reported in the same response

### Requirement: Confirming a capture is available over both interfaces

Both interfaces SHALL be able to confirm a capture into a new purchase, given either the temporary key an image capture returned or the fiscal QR payload a fiscal-only capture was made from, together with the date, amount, expense lines and merchant the caller is asserting. Both SHALL produce the same stored purchase, with the same receipt image and fiscal identity, for the same inputs. A confirmation naming neither a temporary key nor a payload SHALL be rejected identically over both.

#### Scenario: Confirming over either interface

- **WHEN** a capture is confirmed over HTTP, and separately an equivalent capture is confirmed over MCP
- **THEN** both produce a purchase with its receipt attached and in the same extraction state

#### Scenario: Confirming a fiscal-only capture over either interface

- **WHEN** a fiscal-only capture is confirmed by its payload over HTTP, and separately over MCP
- **THEN** both produce a purchase carrying the same fiscal identity, in the same extraction state, with no receipt image

#### Scenario: Confirmation failure is reported identically

- **WHEN** a confirmation whose lines do not reconcile with its amount is submitted over either interface
- **THEN** both reject it and report the same discrepancy
