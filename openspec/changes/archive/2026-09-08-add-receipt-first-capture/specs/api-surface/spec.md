## ADDED Requirements

### Requirement: Confirming a capture is available over both interfaces

Both interfaces SHALL be able to confirm a capture into a new purchase, given the temporary key capture returned and the date, amount, expense lines and merchant the caller is asserting. Both SHALL produce the same stored purchase, with the same receipt attached, for the same inputs.

#### Scenario: Confirming over either interface

- **WHEN** a capture is confirmed over HTTP, and separately an equivalent capture is confirmed over MCP
- **THEN** both produce a purchase with its receipt attached and in the same extraction state

#### Scenario: Confirmation failure is reported identically

- **WHEN** a confirmation whose lines do not reconcile with its amount is submitted over either interface
- **THEN** both reject it and report the same discrepancy

## MODIFIED Requirements

### Requirement: Both interfaces expose the same behaviour

Every ledger operation reachable over HTTP SHALL be reachable over MCP with the same effect, and the reverse, except for receipt capture, receipt image download, and confirming a capture's temporary image bytes directly (as opposed to confirming with the already-captured key), which are HTTP-only. An operation invoked over either interface SHALL produce the same result, the same validation outcome, and the same change to stored data. Neither interface SHALL contain business rules of its own.

#### Scenario: Same operation over either interface

- **WHEN** an identical purchase is recorded over HTTP and, separately from an empty ledger, over MCP
- **THEN** both produce the same stored purchase

#### Scenario: Same validation outcome

- **WHEN** a purchase whose expenses do not sum to its amount is submitted over either interface
- **THEN** both reject it and report the same discrepancy

#### Scenario: Duplicate guard spans interfaces

- **WHEN** a purchase is recorded over MCP
- **AND** the same occurrence and amount is then submitted over HTTP
- **THEN** the existing purchase is returned rather than a new one being created

### Requirement: Receipt capture is HTTP-only

Receipt capture SHALL be accepted over HTTP as a multipart form submission carrying the raw file, with no purchase referenced. The MCP interface SHALL NOT accept image bytes as tool arguments, for capture or for any other operation. MCP SHALL instead be able to confirm a capture by its temporary key, to reference receipts already attached to a purchase, and to trigger and read extraction for them. Neither interface SHALL expose an identifier for a receipt image once it is attached to a purchase: a receipt is addressed by its purchase from that point on.

#### Scenario: Capture over HTTP

- **WHEN** a receipt image is submitted as a multipart form upload with no purchase referenced
- **THEN** the image is stored temporarily and extracted
- **AND** the response identifies it by its temporary key rather than by a purchase or image identifier

#### Scenario: MCP confirms a capture

- **WHEN** an assistant asks over MCP to confirm a capture by its temporary key, supplying the purchase's date, amount and lines
- **THEN** the purchase is created with its receipt attached

#### Scenario: MCP references a stored receipt

- **WHEN** an assistant asks over MCP for the extraction state of the receipt of a purchase
- **THEN** the state and any candidate lines are returned

#### Scenario: MCP triggers extraction

- **WHEN** an assistant asks over MCP to re-run extraction for the receipt of a purchase
- **THEN** extraction runs synchronously and the resulting state is reported in the same response

### Requirement: MCP tool surface

The system SHALL expose MCP tools for recording a purchase, retrieving a purchase, listing purchases over a date range, listing and searching reference data including merchants, confirming a capture into a purchase, and reading and re-running extraction. Each tool SHALL declare a schema for its arguments, and SHALL describe in its own description what it does and when to use it. Reference data SHALL be addressed by code in tool arguments, never by display name.

#### Scenario: Tools are discoverable

- **WHEN** a client lists the available tools
- **THEN** each tool is returned with its argument schema and a description of its purpose

#### Scenario: Recording a purchase over MCP

- **WHEN** the record-purchase tool is called with a valid occurrence, amount and expense lines
- **THEN** the purchase is stored
- **AND** the tool returns the stored purchase including its identifier

#### Scenario: Confirming a capture over MCP

- **WHEN** the confirm-capture tool is called with a temporary key, an amount and expense lines that reconcile
- **THEN** a purchase is created with its receipt already attached

#### Scenario: Reference data addressed by code

- **WHEN** a tool argument names a category by the code GROCERIES
- **THEN** the category is resolved
- **AND** a display name is not accepted in place of a code

### Requirement: The interfaces are independently reachable

The HTTP and MCP interfaces SHALL each be able to run without the other being available. Neither SHALL call the other to do its work.

#### Scenario: MCP without HTTP

- **WHEN** the MCP interface runs while the HTTP interface is not running
- **THEN** every MCP tool that does not involve receipt image bytes works normally, including confirming a capture by its temporary key

#### Scenario: HTTP without MCP

- **WHEN** the HTTP interface runs while the MCP interface is not running
- **THEN** every HTTP operation works normally

### Requirement: Fiscal identifiers may accompany a capture

The HTTP interface SHALL accept fiscal identifiers alongside a multipart capture, so that a client which decoded them at capture time can supply them without waiting for extraction. The MCP interface SHALL be able to read them and to report whether they were corroborated, but SHALL NOT accept image bytes.

#### Scenario: Capture carrying decoded identifiers

- **WHEN** a receipt image is captured together with fiscal identifiers
- **THEN** the temporary capture carries those identifiers
- **AND** they are readable in the capture response before confirmation

#### Scenario: Assistant reads corroboration state

- **WHEN** an assistant asks over MCP about the receipt of a purchase whose identifiers were supplied at capture and later read by extraction
- **THEN** the response states whether the two sources agreed
