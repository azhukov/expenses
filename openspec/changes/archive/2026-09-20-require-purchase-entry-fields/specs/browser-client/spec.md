## ADDED Requirements

### Requirement: The review screen refuses an incomplete purchase

The review screen SHALL NOT submit a confirmation while any value it requires is missing or invalid. It SHALL require a date, a total amount and at least one expense line, and on every line a description of at most 200 characters, an amount, a quantity and a unit. The merchant SHALL remain optional, and a line's category SHALL remain optional.

An amount or quantity SHALL be required to be a number; a blank, a non-numeric entry or a negative value SHALL be treated as invalid. Where the receipt itself established the date, that date SHALL satisfy the requirement without the user entering one.

#### Scenario: Confirming with nothing filled in is refused

- **WHEN** the user confirms with no date, no amount and a single empty line
- **THEN** no request is sent to the ledger
- **AND** the screen reports that each of those values is required

#### Scenario: Every offending value is reported at once

- **WHEN** the user confirms with several invalid values across more than one line
- **THEN** every one of them is reported, not only the first

#### Scenario: A purchase with no lines is refused

- **WHEN** the user removes every line and confirms
- **THEN** no request is sent to the ledger
- **AND** the screen states that at least one expense line is required

#### Scenario: An over-long description is refused

- **WHEN** a line's description is longer than 200 characters
- **AND** the user confirms
- **THEN** no request is sent to the ledger
- **AND** that line's description is reported as too long

#### Scenario: A missing unit is refused

- **WHEN** a line has no unit selected
- **AND** the user confirms
- **THEN** no request is sent to the ledger
- **AND** that line's unit is reported as required

#### Scenario: An optional value left empty does not block confirmation

- **WHEN** the merchant is empty and no line has a category
- **AND** every required value is present and valid
- **THEN** the confirmation is submitted

#### Scenario: A date from the receipt satisfies the requirement

- **WHEN** the capture established an occurred-at date from the receipt and the user has not changed it
- **THEN** the date is not reported as missing
- **AND** confirmation is submitted

### Requirement: Invalid values are marked on the inputs at fault

Where the review screen refuses a confirmation, it SHALL mark each input whose value is at fault, distinguishably from a valid input, and SHALL state beside each what is wrong with it, rather than reporting the problems only as a list away from the fields. A mark SHALL be removed as soon as that input's value becomes valid, without requiring another attempt to confirm. Marking SHALL be conveyed by more than colour alone, and a marked input SHALL be announced as invalid to assistive technology together with its message.

#### Scenario: The faulty input is marked

- **WHEN** confirmation is refused because a line's amount is blank
- **THEN** that line's amount input is marked as invalid and carries a message saying so
- **AND** the inputs whose values are valid are not marked

#### Scenario: A correction clears the mark

- **WHEN** a marked input is given a valid value
- **THEN** its mark and message are removed without the user confirming again

#### Scenario: A marked input is announced as invalid

- **WHEN** an input is marked as invalid
- **THEN** it is exposed to assistive technology as invalid, with its message associated to it

### Requirement: The review screen names the extraction engine

The review screen SHALL show, above the values being reviewed, the name of the extraction engine the capture response reported, so the user knows which reading they are correcting. Where a capture produced no extraction result, the screen SHALL say that no engine reading is available rather than showing an empty or invented name.

#### Scenario: The engine that produced the reading is named

- **WHEN** capture returns a result reporting an engine name
- **THEN** that name is shown at the top of the review screen

#### Scenario: A failed capture states that there is no reading

- **WHEN** capture returns Failed and carries no extraction result
- **THEN** the screen states that no extraction engine reading is available
- **AND** shows no engine name

## MODIFIED Requirements

### Requirement: A rejected confirmation is reported in place

Where confirmation is rejected by the ledger — the lines do not reconcile with the amount, or a value the client could not judge is refused — the review screen SHALL report the specific reason and SHALL leave the user's entered values and the capture in place so they can correct and resubmit, rather than returning to the home screen or discarding what was entered. Rejections the client can determine for itself SHALL be reported before submission instead, on the inputs at fault.

#### Scenario: A reconciliation mismatch is reported

- **WHEN** confirmation is submitted with lines that do not sum to the entered amount
- **THEN** the screen reports the mismatch
- **AND** the entered lines, amount and date remain on the screen for correction

#### Scenario: A missing date never reaches the ledger

- **WHEN** the user confirms with no date entered and the receipt carried no fiscal timestamp
- **THEN** no request is sent to the ledger
- **AND** the date input is marked, stating that a date is required
- **AND** nothing else the user entered is lost
