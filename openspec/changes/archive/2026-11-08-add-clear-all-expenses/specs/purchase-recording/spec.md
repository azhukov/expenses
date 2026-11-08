## ADDED Requirements

### Requirement: The number of recorded purchases can be read

The system SHALL report how many purchases the ledger currently holds, across all dates, without listing them. Reading the count SHALL NOT change stored data.

#### Scenario: Counting a populated ledger

- **WHEN** the ledger holds 12 purchases spread over several months
- **AND** the number of purchases is requested
- **THEN** 12 is reported

#### Scenario: Counting an empty ledger

- **WHEN** no purchases have been recorded
- **AND** the number of purchases is requested
- **THEN** 0 is reported

### Requirement: The ledger can be cleared of all purchases

The system SHALL allow every purchase to be deleted in one operation, together with its expense lines, merchant text, discounts, fiscal identity and receipt reference. Clearing SHALL be all-or-nothing and SHALL report how many purchases it deleted. Categories, units, unit aliases and merchants SHALL NOT be changed by a clear.

#### Scenario: Clearing a populated ledger

- **WHEN** the ledger holds 12 purchases and a clear expecting 12 is requested
- **THEN** 12 purchases are reported as deleted
- **AND** listing purchases over any date range returns nothing
- **AND** retrieving any of the former purchases by its identifier reports that it was not found

#### Scenario: Reference data survives a clear

- **WHEN** the ledger is cleared
- **THEN** every category, unit, unit alias and merchant that existed before the clear still exists, unchanged
- **AND** an alias learned while recording a now-deleted purchase is still present

#### Scenario: Clearing an empty ledger

- **WHEN** no purchases have been recorded and a clear expecting 0 is requested
- **THEN** the clear succeeds and reports 0 purchases deleted

#### Scenario: An interrupted clear deletes nothing

- **WHEN** a clear fails before it completes
- **THEN** every purchase that existed before the clear still exists with all of its expenses

### Requirement: A clear must state how many purchases it expects to delete

A request to clear the ledger SHALL carry the number of purchases the caller expects to delete. A request without it SHALL be rejected as a validation failure. A request whose expected number differs from the number the ledger holds SHALL be rejected as a conflict that reports the current number. A rejected clear SHALL delete nothing.

#### Scenario: No expected number is given

- **WHEN** a clear is requested without an expected number
- **THEN** it is rejected as a validation failure naming the missing value
- **AND** no purchase is deleted

#### Scenario: The ledger changed since it was counted

- **WHEN** the ledger held 12 purchases when counted
- **AND** a 13th purchase is recorded
- **AND** a clear expecting 12 is then requested
- **THEN** it is rejected as a conflict reporting that the ledger now holds 13
- **AND** no purchase is deleted

#### Scenario: A negative expected number

- **WHEN** a clear expecting -1 is requested
- **THEN** it is rejected as a validation failure
- **AND** no purchase is deleted

### Requirement: The duplicate guard does not see cleared purchases

After a clear, a submission matching the occurrence and amount of a purchase that was deleted SHALL create a new purchase rather than being absorbed as a duplicate.

#### Scenario: Re-recording a cleared purchase

- **WHEN** a purchase occurring 2026-08-19 10:15 for 23.40 is recorded
- **AND** the ledger is cleared
- **AND** a purchase occurring 2026-08-19 10:15 for 23.40 is submitted again
- **THEN** a new purchase is created rather than an existing one returned
