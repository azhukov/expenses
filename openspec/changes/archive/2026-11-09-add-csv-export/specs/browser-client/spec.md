## ADDED Requirements

### Requirement: The ledger can be exported from the home screen

The home screen SHALL offer an "Export" control, presented as quietly as the other tools so that capture stays the prominent control. Activating it SHALL NOT download anything by itself. It SHALL open an export step where the user chooses what to export. Cancelling that step SHALL send nothing.

#### Scenario: The control is reachable from home

- **WHEN** the user is on the home screen
- **THEN** an "Export" control is present alongside the other tools
- **AND** the capture action remains the most prominent control

#### Scenario: Activating the control downloads nothing

- **WHEN** the user activates "Export"
- **THEN** an export step is shown
- **AND** no export has been requested from the ledger

#### Scenario: Cancelling sends nothing

- **WHEN** the user opens the export step and cancels it
- **THEN** no export is requested
- **AND** the home screen is as it was

### Requirement: The export step chooses everything, a month or a date range

The export step SHALL offer three choices: everything, one month, or a date range. It SHALL start on one month, set to the current month. The month SHALL be changeable a month at a time, and SHALL NOT go past the current month. A date range SHALL need both a start and an end. A range whose end is before its start SHALL be marked on the inputs and SHALL NOT be sent.

#### Scenario: The current month is the default

- **WHEN** the user opens the export step during October 2026
- **THEN** the month choice is selected and shows October 2026

#### Scenario: Exporting a month

- **WHEN** the user selects September 2026 and confirms
- **THEN** the client requests an export from 2026-09-01 to 2026-09-30

#### Scenario: Exporting everything

- **WHEN** the user chooses everything and confirms
- **THEN** the client requests an export with no range

#### Scenario: Exporting a date range

- **WHEN** the user chooses a range from 2026-10-05 to 2026-10-12 and confirms
- **THEN** the client requests an export from 2026-10-05 to 2026-10-12

#### Scenario: An incomplete range cannot be sent

- **WHEN** the user chooses a range and gives a start but no end
- **THEN** the export cannot be confirmed

#### Scenario: An inverted range is refused before sending

- **WHEN** the user chooses a range whose end is before its start
- **THEN** the inputs are marked as invalid with a message saying the end falls before the start
- **AND** no export is requested

### Requirement: A confirmed export is saved as a file

Confirming the export step SHALL save the ledger's CSV as a file on the device, named for what was chosen. The step SHALL show that the export is in progress and SHALL prevent a second request while it is. Once the file is saved, the step SHALL close. The client SHALL NOT alter the file's contents.

#### Scenario: Files are named for what was chosen

- **WHEN** the user exports everything, then October 2026, then the range 2026-10-05 to 2026-10-12
- **THEN** the files are named `expenses-all.csv`, `expenses-2026-10.csv` and `expenses-2026-10-05-to-2026-10-12.csv`

#### Scenario: An export in progress

- **WHEN** the user confirms an export and the ledger has not yet answered
- **THEN** the step shows that the export is in progress
- **AND** it cannot be confirmed again

#### Scenario: The file is the ledger's file

- **WHEN** an export is saved
- **THEN** the saved file's bytes equal the body the ledger returned

### Requirement: A failed export is reported in the export step

Where the ledger rejects an export or cannot be reached, the client SHALL report it in the export step using the ledger's own message where it has one. It SHALL save no file, and SHALL keep the user's choice so that they can retry or change it.

#### Scenario: The ledger rejects the export

- **WHEN** the user confirms an export and the ledger answers with a validation failure
- **THEN** the export step shows the ledger's message
- **AND** no file is saved
- **AND** the chosen range is still selected

#### Scenario: The ledger cannot be reached

- **WHEN** the user confirms an export and the request fails to reach the ledger
- **THEN** the export step reports that the ledger could not be reached
- **AND** the user can confirm again
