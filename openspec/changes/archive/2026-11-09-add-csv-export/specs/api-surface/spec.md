## MODIFIED Requirements

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

## ADDED Requirements

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
