## MODIFIED Requirements

### Requirement: Both interfaces expose the same behaviour

Every ledger operation reachable over HTTP SHALL be reachable over MCP with the same effect, and the reverse, except for receipt capture, receipt image download, confirming a capture's temporary image bytes directly (as opposed to confirming with the already-captured key), and clearing the ledger of all purchases, which are HTTP-only. An operation invoked over either interface SHALL produce the same result, the same validation outcome, and the same change to stored data. Neither interface SHALL contain business rules of its own.

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

## ADDED Requirements

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
