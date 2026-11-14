# api-surface Specification

## Purpose

Defines the two front doors onto the ledger — an HTTP interface for the browser client and a Model Context Protocol interface for assistants — and the guarantee that both expose the same behaviour because both delegate to the same underlying operations.

## Requirements

### Requirement: Both interfaces expose the same behaviour

Every ledger operation reachable over HTTP SHALL be reachable over MCP with the same effect, and the reverse, except for receipt capture, receipt image download, confirming a capture's temporary image bytes directly (as opposed to confirming with the already-captured key), clearing the ledger of all purchases, and exporting the ledger as a file, which are HTTP-only. An operation invoked over either interface SHALL produce the same result, the same validation outcome, and the same change to stored data. Neither interface SHALL contain business rules of its own.

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

#### Scenario: Clearing is not offered over MCP

- **WHEN** the MCP tools are listed
- **THEN** no tool deletes purchases, singly or all at once

#### Scenario: Exporting is not offered over MCP

- **WHEN** the MCP tools are listed
- **THEN** no tool exports the ledger as a file
- **AND** purchases in a date range remain readable through the listing tool

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
- **THEN** every MCP tool that does not involve receipt image bytes works normally, including confirming a capture by its temporary key

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

### Requirement: Browser origins are allowed by configuration

The HTTP interface SHALL accept cross-origin browser requests only from origins listed in its configuration, and SHALL match those origins exactly (scheme, host and port). Where no origins are configured, it SHALL allow no cross-origin browser request. It SHALL answer preflight requests from an allowed origin for every method and request header the interface uses, and SHALL NOT allow credentials. Responses to an allowed origin SHALL carry the cross-origin headers whatever their status, so an error in the interface's error shape is as readable to a browser client as a success. Where a configured origin is not a plain `http` or `https` origin (a wildcard, a value with a path, query or fragment, or any other scheme), the host SHALL refuse to start and SHALL name the offending value. Requests without an `Origin` header, such as those from the MCP host or a command-line tool, SHALL be unaffected.

#### Scenario: An allowed origin reads the ledger

- **WHEN** `http://localhost:5173` is configured as an allowed origin
- **AND** a request from that origin lists categories
- **THEN** the response allows `http://localhost:5173` as its origin

#### Scenario: An unlisted origin is not allowed

- **WHEN** a request from `http://elsewhere.test` lists categories
- **AND** that origin is not configured
- **THEN** the response does not allow that origin

#### Scenario: Origins match exactly

- **WHEN** `http://localhost:5173` is configured
- **AND** a request comes from `https://localhost:5173` or `http://localhost:4173`
- **THEN** the response does not allow that origin

#### Scenario: Nothing configured allows nothing

- **WHEN** no origins are configured
- **AND** a request from any origin lists categories
- **THEN** the response does not allow that origin

#### Scenario: A preflight for a write is answered

- **WHEN** an allowed origin sends a preflight for a `POST` with a JSON content type
- **THEN** the preflight response allows that origin, that method and that header

#### Scenario: Methods beyond POST are allowed

- **WHEN** an allowed origin sends a preflight for a `PUT` or a `DELETE`
- **THEN** the preflight response allows that method

#### Scenario: An error response is readable cross-origin

- **WHEN** a request from an allowed origin fails validation, names a resource that does not exist, or fails unexpectedly
- **THEN** the response carries the error shape
- **AND** it allows that origin

#### Scenario: Credentials are not allowed

- **WHEN** an allowed origin sends a request
- **THEN** the response does not allow credentials

#### Scenario: A wildcard origin stops the host

- **WHEN** the host is started with `*` configured as an allowed origin
- **THEN** it does not start
- **AND** the failure names the offending value

#### Scenario: An origin with a path stops the host

- **WHEN** the host is started with `http://localhost:5173/app` configured as an allowed origin
- **THEN** it does not start
- **AND** the failure names the offending value

#### Scenario: Requests without an origin are unaffected

- **WHEN** a request with no `Origin` header lists categories
- **THEN** it succeeds exactly as it did before cross-origin access was configured

### Requirement: The HTTP interface can be served over HTTPS

The HTTP interface SHALL be servable over HTTPS when a certificate and an HTTPS port are configured, without any change to the host itself. Responses over HTTPS SHALL be the same as over HTTP, including the error shape and the cross-origin headers for an allowed origin. Where HTTPS is configured, plain HTTP SHALL continue to be answered on its own port and SHALL NOT be redirected to HTTPS. Where no certificate is configured, the interface SHALL be served over HTTP alone, as before. Where a configured certificate cannot be read, the host SHALL refuse to start.

#### Scenario: A configured certificate serves HTTPS

- **WHEN** the host is started with a certificate and an HTTPS port configured
- **AND** categories are listed over HTTPS on that port
- **THEN** the request succeeds with the same body as over HTTP
- **AND** the connection presents the configured certificate

#### Scenario: HTTP is not redirected

- **WHEN** HTTPS is configured
- **AND** categories are listed over plain HTTP
- **THEN** the request succeeds over HTTP with no redirect

#### Scenario: Cross-origin headers over HTTPS

- **WHEN** a request over HTTPS comes from an allowed `https` origin on another host
- **THEN** the response allows that origin

#### Scenario: No certificate means HTTP only

- **WHEN** the host is started without a certificate configured
- **THEN** it serves HTTP as before and listens on no HTTPS port

#### Scenario: An unreadable certificate stops the host

- **WHEN** the host is started with a certificate path that does not exist or does not contain a usable certificate
- **THEN** it does not start

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

### Requirement: Applying migrations on startup is a setting

The HTTP host SHALL apply pending database migrations when it starts if, and only if, it runs in the `Development` environment or its configuration explicitly turns migrating on startup on. In every other case it SHALL apply none. In every case it SHALL still verify the database's provisioning before it starts answering requests, whether or not it migrated.

#### Scenario: Development migrates by default

- **WHEN** the host starts in the `Development` environment with no migration setting
- **THEN** pending migrations are applied before it answers requests

#### Scenario: Other environments do not migrate by default

- **WHEN** the host starts in an environment other than `Development` with no migration setting
- **THEN** no migration is applied

#### Scenario: The setting turns migrating on

- **WHEN** the host starts in an environment other than `Development` with migrating on startup turned on
- **THEN** pending migrations are applied before it answers requests

#### Scenario: Provisioning is verified either way

- **WHEN** the host starts against a database created without the required collation
- **THEN** it does not start, whether or not migrating on startup is on

### Requirement: The host has a Railway environment

The HTTP host SHALL define a `Railway` environment. When it starts in that environment, it SHALL allow `https://expenses-frontend-production-2e52.up.railway.app` as its only browser origin, and it SHALL apply pending migrations on startup. All connection details and secrets SHALL come from the environment the host is started with, and SHALL NOT be committed in that environment's configuration.

#### Scenario: The Railway client's origin reads the ledger

- **WHEN** the host runs in the `Railway` environment
- **AND** a request from `https://expenses-frontend-production-2e52.up.railway.app` lists categories
- **THEN** the response allows that origin

#### Scenario: Local development origins are not allowed on Railway

- **WHEN** the host runs in the `Railway` environment
- **AND** a request from `http://localhost:5173` lists categories
- **THEN** the response does not allow that origin

#### Scenario: Railway migrates on startup

- **WHEN** the host starts in the `Railway` environment against a database with pending migrations
- **THEN** the migrations are applied before it answers requests

### Requirement: Unit aliases are available over both interfaces

Both interfaces SHALL list units with their aliases, add an alias to a unit, and remove an alias from a unit, with the same effect and the same validation outcome. Units SHALL be addressed by code and aliases by their text; neither interface SHALL require an alias identifier. Adding an alias that already maps to the same unit SHALL be reported as a success, so that repeating the call is safe. Rejections SHALL use the existing error shape and SHALL name the offending alias and, where the alias already maps elsewhere, the unit it maps to.

#### Scenario: Listing units over HTTP carries aliases

- **WHEN** the unit listing is requested over HTTP
- **THEN** each unit is returned with its aliases

#### Scenario: Listing units over MCP carries aliases

- **WHEN** the list-units tool is called
- **THEN** each unit is returned with its aliases

#### Scenario: Adding an alias over HTTP

- **WHEN** the alias "kut" is submitted over HTTP for the unit code PACK
- **THEN** the response reports success and carries the unit PACK with its aliases, including "kut"

#### Scenario: Adding an alias over MCP

- **WHEN** the add-unit-alias tool is called with the unit code PACK and the alias "kut"
- **THEN** the tool returns the unit PACK with its aliases, including "kut"

#### Scenario: Repeating an add is safe

- **WHEN** the add-unit-alias tool is called twice with the unit code PCS and the alias "kom"
- **THEN** both calls succeed
- **AND** "kom" maps to PCS once

#### Scenario: A conflicting alias is rejected the same way on both interfaces

- **WHEN** "kom" maps to PCS and a request over either interface adds "kom" to PACK
- **THEN** both interfaces reject it with the same stable error code
- **AND** the error names the alias "kom" and the unit PCS

#### Scenario: Removing an alias over HTTP

- **WHEN** removal of the alias "kd" from the unit code PCS is requested over HTTP
- **THEN** the response reports success
- **AND** "kd" no longer maps to any unit

#### Scenario: Removing an alias over MCP

- **WHEN** the remove-unit-alias tool is called with the unit code PCS and the alias "kd"
- **THEN** the tool reports success
- **AND** "kd" no longer maps to any unit

#### Scenario: Aliases are addressed in normalised form

- **WHEN** removal of the alias "KD." from the unit code PCS is requested over either interface
- **THEN** the alias "kd" is removed

#### Scenario: Units cannot be created through alias operations

- **WHEN** an alias is submitted for a unit code that does not exist
- **THEN** the request is rejected as unit not found
- **AND** no unit is created

### Requirement: Purchases can be counted over both interfaces

Both interfaces SHALL report the number of purchases the ledger holds, with the same result.

#### Scenario: Counting over HTTP

- **WHEN** the ledger holds 12 purchases and their count is requested over HTTP
- **THEN** the response reports 12

#### Scenario: Counting over MCP

- **WHEN** the ledger holds 12 purchases and the count-purchases tool is called
- **THEN** the tool reports 12

### Requirement: The ledger can be cleared over HTTP

The HTTP interface SHALL clear the ledger when asked with the number of purchases the caller expects to delete, and SHALL report how many were deleted. A missing or invalid expected number SHALL be reported as a validation failure, and a stale one as a conflict carrying the current count, both in the existing error shape.

#### Scenario: Clearing over HTTP

- **WHEN** the ledger holds 12 purchases and a clear expecting 12 is requested over HTTP
- **THEN** the response reports success and that 12 purchases were deleted

#### Scenario: A clear without an expected number over HTTP

- **WHEN** a clear is requested over HTTP without an expected number
- **THEN** the response is a validation failure with a stable error code naming the missing value
- **AND** no purchase is deleted

#### Scenario: A stale clear over HTTP

- **WHEN** the ledger holds 13 purchases and a clear expecting 12 is requested over HTTP
- **THEN** the response reports a conflict with a stable error code and states that the ledger holds 13
- **AND** no purchase is deleted

#### Scenario: Clearing from an allowed browser origin

- **WHEN** an allowed origin sends a preflight for the clear request and then the request itself
- **THEN** the preflight is answered and the clear's response is readable by that origin

### Requirement: The ledger can be exported over HTTP

The HTTP interface SHALL export the ledger as CSV when asked, for every purchase or for an inclusive date range given by optional start and end dates. A successful export SHALL be a `text/csv` response with a UTF-8 charset, offered as an attachment with a file name that reflects the range. An invalid range SHALL be reported in the existing error shape, as JSON, not as CSV.

#### Scenario: Exporting everything over HTTP

- **WHEN** an export without a range is requested over HTTP
- **THEN** the response succeeds with content type `text/csv; charset=utf-8`
- **AND** it is offered as an attachment named `expenses-all.csv`

#### Scenario: Exporting a range over HTTP

- **WHEN** an export from 2026-10-01 to 2026-10-31 is requested over HTTP
- **THEN** the response succeeds with the CSV of that range
- **AND** it is offered as an attachment named `expenses-2026-10-01-to-2026-10-31.csv`

#### Scenario: An open-ended range is named for its open end

- **WHEN** an export from 2026-10-15 with no end is requested over HTTP
- **THEN** it is offered as an attachment named `expenses-2026-10-15-to-latest.csv`

#### Scenario: An inverted range over HTTP

- **WHEN** an export from 2026-10-31 to 2026-10-01 is requested over HTTP
- **THEN** the response is a validation failure in the existing JSON error shape, with the listing's stable error code for an inverted range

#### Scenario: A malformed date over HTTP

- **WHEN** an export is requested with a start that is not a date
- **THEN** the response is a validation failure in the existing JSON error shape naming the start

#### Scenario: Exporting from an allowed browser origin

- **WHEN** an allowed origin requests an export
- **THEN** the response body is readable by that origin

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
- **AND** it carries the request sent to the verification service: its method, address and form fields

#### Scenario: No request where none was sent

- **WHEN** a capture's payload is not a fiscal code, or lacks a parameter the verification service needs
- **THEN** the fiscal diagnosis carries no request

#### Scenario: Reading an unreadable answer

- **WHEN** the verification service answers a capture's request with a body that is not a readable invoice
- **THEN** the fiscal diagnosis carries an excerpt of that body no longer than 200 characters, never the whole of it

#### Scenario: A retrieved invoice

- **WHEN** a capture's invoice is retrieved from the verification service
- **THEN** the fiscal diagnosis kind states that it was retrieved
