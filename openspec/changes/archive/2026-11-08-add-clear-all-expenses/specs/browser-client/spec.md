## ADDED Requirements

### Requirement: The ledger can be cleared from the home screen

The home screen SHALL offer a "Clear all" control, presented as quietly as the other tools so that capture stays the prominent control. Activating it SHALL NOT delete anything by itself: it SHALL open a confirmation step. Where the ledger holds no purchases at all, the confirmation step SHALL say there is nothing to clear and SHALL offer no way to confirm.

#### Scenario: The control is reachable from home

- **WHEN** the user is on the home screen and the ledger holds purchases
- **THEN** a "Clear all" control is present alongside the other tools
- **AND** the capture action remains the most prominent control

#### Scenario: Activating the control deletes nothing

- **WHEN** the user activates "Clear all"
- **THEN** a confirmation step is shown
- **AND** no request to clear the ledger has been sent

#### Scenario: Nothing to clear

- **WHEN** the ledger holds no purchases in any month and the user activates "Clear all"
- **THEN** the confirmation step states that there is nothing to clear
- **AND** offers no way to confirm a deletion

### Requirement: Clearing is confirmed against the number of purchases

The confirmation step SHALL state, before the user confirms, how many purchases will be deleted across all dates, that reference data is kept, and that the deletion cannot be undone. Confirming SHALL clear exactly that number. Cancelling SHALL send nothing and change nothing.

#### Scenario: The number to be deleted is shown

- **WHEN** the ledger holds 12 purchases, 3 of them this month, and the user activates "Clear all"
- **THEN** the confirmation states that 12 purchases will be deleted
- **AND** states that the deletion cannot be undone

#### Scenario: Confirming clears the ledger

- **WHEN** the user confirms the deletion of 12 purchases
- **THEN** the client asks the ledger to clear, expecting 12
- **AND** once the ledger reports success the home screen shows the empty-ledger state and a month total of zero

#### Scenario: Cancelling changes nothing

- **WHEN** the user cancels the confirmation
- **THEN** no clear is requested
- **AND** the home screen shows the purchases exactly as before

#### Scenario: The count cannot be read

- **WHEN** the user activates "Clear all" and the number of purchases cannot be read
- **THEN** the confirmation reports that failure and offers no way to confirm
- **AND** the rest of the home screen stays usable

### Requirement: A rejected clear is reported in the confirmation step

Where the ledger rejects or cannot be reached for a clear, the client SHALL report it in the confirmation step using the ledger's own message where it has one, and SHALL leave the ledger view as it was. Where the rejection reports that the count has changed, the confirmation SHALL show the new count and require the user to confirm again.

#### Scenario: Something was recorded meanwhile

- **WHEN** the confirmation shows 12 purchases and a 13th is recorded elsewhere before the user confirms
- **AND** the user confirms
- **THEN** nothing is deleted
- **AND** the confirmation now states that 13 purchases will be deleted and awaits a new confirmation

#### Scenario: The ledger cannot be reached

- **WHEN** the user confirms and the clear request fails to reach the ledger
- **THEN** the confirmation reports that the ledger could not be reached
- **AND** the purchases are still listed on the home screen once the confirmation is dismissed
