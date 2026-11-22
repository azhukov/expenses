## MODIFIED Requirements

### Requirement: A successful capture is presented for review

Where capture returns Extracted or NeedsReview, the screen SHALL present the extraction's candidate expense lines, amount, merchant and occurred-at date, and SHALL let the user edit any of them before confirming. The client SHALL carry the capture's temporary key and reported extraction outcome forward itself, since the server holds neither once the capture response is returned. Where a candidate line's unit was inferred from its quantity, the screen SHALL show that the unit is a guess, SHALL clear that mark when the user chooses a unit for the line, and SHALL send the line as inferred on confirmation only while the mark stands. Editing the quantity SHALL NOT change an inferred unit.

#### Scenario: Candidates are shown for review

- **WHEN** capture returns Extracted or NeedsReview
- **THEN** the candidate expense lines, amount, merchant and occurred-at date are shown

#### Scenario: Candidates are editable

- **WHEN** the review screen is showing candidate lines
- **THEN** the user can change the description, amount, quantity or category of any line, add a line, or remove one

#### Scenario: The capture result is not re-requested

- **WHEN** the user edits candidates and later confirms
- **THEN** the temporary key and the extraction outcome sent with confirmation are the ones the original capture response carried, not a freshly re-fetched result

#### Scenario: An inferred unit is shown as a guess

- **WHEN** a candidate line's unit was inferred from its quantity
- **THEN** the line shows its unit together with an indication that it was guessed from the quantity
- **AND** a line whose unit was matched carries no such indication

#### Scenario: An untouched inferred unit is confirmed as inferred

- **WHEN** the user confirms without changing a line's inferred unit
- **THEN** the confirmation sends that line with its unit and marked as inferred

#### Scenario: Choosing a unit clears the guess

- **WHEN** the user chooses a unit for a line whose unit was inferred
- **THEN** the indication is removed
- **AND** the confirmation sends that line with the chosen unit, not marked as inferred

#### Scenario: Editing the quantity does not re-infer

- **WHEN** a line's unit PCS was inferred from the quantity 1
- **AND** the user changes the quantity to 0.482
- **THEN** the line's unit is still PCS, still shown as a guess

### Requirement: The review screen refuses an incomplete purchase

The review screen SHALL NOT submit a confirmation while any value it requires is missing or invalid. It SHALL require a date, a total amount and at least one expense line, and on every line a description of at most 200 characters, an amount, a quantity and a unit. A line whose unit is PCS SHALL be required to have a whole-number quantity. This rule SHALL be applied by the review screen only and SHALL NOT be imposed by the ledger. The merchant SHALL remain optional, and a line's category SHALL remain optional.

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

#### Scenario: Pieces with a fractional quantity are refused

- **WHEN** a line has the unit PCS and the quantity 0.482
- **AND** the user confirms
- **THEN** no request is sent to the ledger
- **AND** that line's unit is reported as needing a whole-number quantity

#### Scenario: Pieces with a whole quantity are accepted

- **WHEN** a line has the unit PCS and the quantity 3
- **AND** every other required value is present and valid
- **THEN** the confirmation is submitted

#### Scenario: A fiscal line printed in pieces with a fractional quantity must be corrected

- **WHEN** a fiscal invoice line printed "KOM" with the quantity 0.350 was matched to PCS
- **AND** the user confirms without changing it
- **THEN** no request is sent to the ledger
- **AND** that line's unit is reported as needing a whole-number quantity

#### Scenario: Other units allow fractional quantities

- **WHEN** a line has the unit PACK and the quantity 0.5
- **AND** every other required value is present and valid
- **THEN** the confirmation is submitted
