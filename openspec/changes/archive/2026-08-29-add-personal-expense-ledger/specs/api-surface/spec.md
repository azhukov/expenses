## Purpose

Defines the two front doors onto the ledger — an HTTP interface for the browser client and a Model Context Protocol interface for assistants — and the guarantee that both expose the same behaviour because both delegate to the same underlying operations.

## ADDED Requirements

### Requirement: Both interfaces expose the same behaviour

Every ledger operation reachable over HTTP SHALL be reachable over MCP with the same effect, and the reverse, except for receipt image upload and download which are HTTP-only. An operation invoked over either interface SHALL produce the same result, the same validation outcome, and the same change to stored data. Neither interface SHALL contain business rules of its own.

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

### Requirement: HTTP interface for the browser client

The system SHALL expose an HTTP interface covering recording, retrieving and listing purchases, uploading and retrieving receipt images, working with extraction candidates, and listing and managing reference data including merchants. It SHALL use conventional HTTP status codes and SHALL return errors in a single consistent machine-readable shape carrying a stable error code and a human-readable message.

#### Scenario: Successful creation

- **WHEN** a valid new purchase is submitted over HTTP
- **THEN** the response reports creation and includes the stored purchase with its identifier

#### Scenario: Duplicate submission

- **WHEN** a purchase matching an existing occurrence and amount is submitted over HTTP
- **THEN** the response reports success rather than an error
- **AND** it is distinguishable from the creation of a new purchase
- **AND** it carries the existing purchase

#### Scenario: Validation failure shape

- **WHEN** an invalid request is submitted over HTTP
- **THEN** the response carries a stable error code, a human-readable message, and the offending fields
- **AND** every validation failure across the interface uses that same shape

#### Scenario: Unknown resource

- **WHEN** a purchase that does not exist is requested over HTTP
- **THEN** the response reports that it was not found, in the same error shape

### Requirement: Receipt upload is HTTP-only

Receipt image upload SHALL be accepted over HTTP as a multipart form submission carrying the raw file, addressed to the purchase the receipt belongs to. The MCP interface SHALL NOT accept image bytes as tool arguments. MCP SHALL instead be able to reference receipts already stored, and to trigger and read extraction for them. Neither interface SHALL expose an identifier for a receipt image: a receipt is addressed by its purchase.

#### Scenario: Upload over HTTP

- **WHEN** a receipt image is submitted as a multipart form upload for a purchase
- **THEN** the image is stored and attached to that purchase
- **AND** the response identifies it by the purchase rather than by an image identifier

#### Scenario: MCP references a stored receipt

- **WHEN** an assistant asks over MCP for the extraction state of the receipt of a purchase
- **THEN** the state and any candidate lines are returned

#### Scenario: MCP triggers extraction

- **WHEN** an assistant asks over MCP to re-run extraction for the receipt of a purchase
- **THEN** extraction is started and the resulting state is reported

### Requirement: MCP tool surface

The system SHALL expose MCP tools for recording a purchase, retrieving a purchase, listing purchases over a date range, listing and searching reference data including merchants, and reading and re-running extraction. Each tool SHALL declare a schema for its arguments, and SHALL describe in its own description what it does and when to use it. Reference data SHALL be addressed by code in tool arguments, never by display name.

#### Scenario: Tools are discoverable

- **WHEN** a client lists the available tools
- **THEN** each tool is returned with its argument schema and a description of its purpose

#### Scenario: Recording a purchase over MCP

- **WHEN** the record-purchase tool is called with a valid occurrence, amount and expense lines
- **THEN** the purchase is stored
- **AND** the tool returns the stored purchase including its identifier

#### Scenario: Reference data addressed by code

- **WHEN** a tool argument names a category by the code GROCERIES
- **THEN** the category is resolved
- **AND** a display name is not accepted in place of a code

### Requirement: Repeated tool calls are safe

An MCP tool that records a purchase SHALL be safe to call more than once with the same arguments. A repeated call SHALL return a successful result describing the already-recorded purchase, and SHALL NOT be reported as an error condition that an assistant would try to work around.

#### Scenario: Assistant repeats a call

- **WHEN** the record-purchase tool is called twice with identical arguments
- **THEN** both calls succeed
- **AND** the second states that the purchase was already recorded and identifies it
- **AND** exactly one purchase exists

#### Scenario: Genuine failures are still errors

- **WHEN** the record-purchase tool is called with expenses that do not sum to the purchase amount
- **THEN** the call reports an error explaining the discrepancy

### Requirement: Errors carry enough detail to be acted on

Failures over either interface SHALL identify what was rejected and why, in terms of the submitted values. A failure SHALL NOT expose internal implementation detail, stack traces, or database identifiers that are not part of the interface.

#### Scenario: Actionable validation error

- **WHEN** a purchase is rejected because its expenses do not sum to its amount
- **THEN** the error states the submitted purchase amount and the computed sum

#### Scenario: Internal detail is not leaked

- **WHEN** an unexpected internal failure occurs
- **THEN** the response reports a generic failure with a correlation identifier
- **AND** it contains no stack trace or internal implementation detail

### Requirement: The interfaces are independently reachable

The HTTP and MCP interfaces SHALL each be able to run without the other being available. Neither SHALL call the other to do its work.

#### Scenario: MCP without HTTP

- **WHEN** the MCP interface runs while the HTTP interface is not running
- **THEN** every MCP tool that does not involve image upload works normally

#### Scenario: HTTP without MCP

- **WHEN** the HTTP interface runs while the MCP interface is not running
- **THEN** every HTTP operation works normally

### Requirement: Merchant and discount are carried by both interfaces

Both interfaces SHALL accept a merchant when a purchase is recorded, and SHALL accept a list unit price and discount amount on an expense line. Both SHALL return the merchant, the verbatim merchant text, the discount values, and the derived saving when a purchase is retrieved. Neither interface SHALL accept a discount percentage in place of a discount amount.

#### Scenario: Recording a merchant over either interface

- **WHEN** a purchase naming a merchant is recorded over HTTP and, separately from an empty ledger, over MCP
- **THEN** both produce the same stored purchase with the same merchant and the same verbatim merchant text

#### Scenario: Discount round-trips

- **WHEN** a purchase whose lines carry list prices and discounts is recorded and then retrieved
- **THEN** the list prices and discount amounts are returned unchanged
- **AND** the purchase reports its total saving

#### Scenario: Discount percentage is rejected as input

- **WHEN** an expense line is submitted carrying a discount percentage instead of a discount amount
- **THEN** the request is rejected over both interfaces with the same error

#### Scenario: Merchants are addressed unambiguously over MCP

- **WHEN** an assistant records a purchase naming a merchant that does not yet exist
- **THEN** the merchant is created and the result states that it was newly added
- **AND** a subsequent identical naming resolves to the same merchant without creating another

### Requirement: Extraction results are legible over both interfaces

Both interfaces SHALL report, for an extraction, the stages that ran, the stage that produced each value, the outcome of each arithmetic check, and any fiscal identifiers with whether they were corroborated. A result produced by a placeholder engine SHALL be identifiable as such wherever it is surfaced. Because candidates are not persisted, both interfaces SHALL be able to report a purchase whose candidates are no longer held: the recorded extraction state SHALL still be returned, the absence of candidates SHALL be distinguishable from an extraction that produced none, and neither interface SHALL start extraction in response to a read.

#### Scenario: Reading a validated extraction

- **WHEN** an extraction result is retrieved over either interface
- **THEN** it reports each arithmetic check and whether it passed, failed, or did not apply

#### Scenario: Reading a failed check

- **WHEN** an extraction whose lines do not sum to its total is retrieved
- **THEN** the response names the extracted total and the computed sum

#### Scenario: Placeholder results stay identifiable

- **WHEN** candidates produced by a placeholder stage are retrieved over either interface
- **THEN** both identify the engine and stage that produced them

#### Scenario: Reading an image whose candidates are no longer held

- **WHEN** the extraction of a purchase whose candidates were discarded on restart is read over either interface
- **THEN** both report the recorded extraction state and that no candidates are available
- **AND** neither starts extraction
- **AND** the response distinguishes this from an extraction that produced no lines

### Requirement: Fiscal identifiers may accompany an upload

The HTTP interface SHALL accept fiscal identifiers alongside a multipart receipt upload, so that a client which decoded them at capture can supply them without waiting for extraction. The MCP interface SHALL be able to read them and to report whether they were corroborated, but SHALL NOT accept image bytes.

#### Scenario: Upload carrying decoded identifiers

- **WHEN** a receipt image is uploaded together with fiscal identifiers
- **THEN** the image is stored with those identifiers attached
- **AND** they are readable before extraction has run

#### Scenario: Assistant reads corroboration state

- **WHEN** an assistant asks over MCP about a stored receipt image whose identifiers were supplied at upload and later read by extraction
- **THEN** the response states whether the two sources agreed
