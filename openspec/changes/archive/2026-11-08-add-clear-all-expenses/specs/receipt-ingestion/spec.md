## ADDED Requirements

### Requirement: Clearing the ledger disposes of receipts under the sharing rule

When the ledger is cleared, the system SHALL remove from the permanent receipt store each file that no remaining purchase references, only after the references to it are gone. Unconfirmed extraction candidates held for a cleared purchase SHALL be discarded. A file still referenced by a purchase recorded after the clear SHALL be kept.

#### Scenario: Receipt files go with the cleared purchases

- **WHEN** the ledger holds purchases carrying receipt images and is cleared
- **THEN** their files are no longer in the permanent receipt store

#### Scenario: Candidates go with the cleared purchases

- **WHEN** a purchase has unconfirmed extraction candidates and the ledger is cleared
- **THEN** no candidates are held for that purchase's identifier

#### Scenario: Interrupted file removal leaves no broken reference

- **WHEN** a clear is interrupted after the purchases are deleted and before every file is removed
- **THEN** the ledger contains no reference to the remaining files
- **AND** the remaining files are unreferenced and safe to discard

### Requirement: Clearing the ledger leaves pending captures confirmable

A clear SHALL NOT remove temporary captures awaiting confirmation. A capture taken before a clear SHALL remain confirmable afterwards, subject to the existing daily cleanup.

#### Scenario: Confirming a capture taken before a clear

- **WHEN** an image is captured and not yet confirmed
- **AND** the ledger is cleared
- **AND** the capture is then confirmed
- **THEN** a purchase carrying that receipt is created

### Requirement: A cleared invoice is no longer reported as already recorded

After a clear, capturing a receipt whose invoice identification code matched a cleared purchase SHALL NOT report that the invoice is already recorded.

#### Scenario: Scanning a cleared invoice again

- **WHEN** a purchase carrying the fiscal invoice code "ABC123" is recorded
- **AND** the ledger is cleared
- **AND** a receipt with the invoice code "ABC123" is captured
- **THEN** the capture names no purchase as already recording that invoice
