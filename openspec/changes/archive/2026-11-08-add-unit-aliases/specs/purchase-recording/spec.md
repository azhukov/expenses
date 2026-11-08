## MODIFIED Requirements

### Requirement: Expense line detail

Each expense SHALL carry a description, a quantity, an amount and a unit. A description SHALL be at most 200 characters after surrounding whitespace is removed, and an expense whose description exceeds that SHALL be rejected rather than truncated. A unit SHALL be established, in this order, by the unit code the caller supplies, by a unit matched while extracting the line, or by the line's printed unit text matching an active unit's code or alias; an expense that establishes none of these SHALL be rejected, and the rejection SHALL name the line at fault. Each expense MAY carry a unit price and a category. Quantity SHALL default to 1 when not supplied and SHALL NOT be null. Quantity SHALL support fractional values to at least three decimal places.

Expenses already recorded without a unit SHALL remain readable and SHALL NOT be altered by this rule; it governs what may be newly recorded.

#### Scenario: Quantity defaults to one

- **WHEN** an expense is recorded without a quantity
- **THEN** the stored expense has quantity 1

#### Scenario: Fractional quantity

- **WHEN** an expense of 0.482 kg of bananas at 2.19 per kg for 1.06 is recorded
- **THEN** the stored expense has quantity 0.482, unit kg, unit price 2.19 and amount 1.06

#### Scenario: Quantity and unit price need not multiply to the amount

- **WHEN** an expense with quantity 3, unit price 0.333 and amount 1.00 is recorded
- **THEN** the expense is accepted
- **AND** the amount 1.00 is treated as authoritative

#### Scenario: An expense without a unit is rejected

- **WHEN** a purchase is recorded whose expense line supplies no unit code, was matched to no unit, and carries no printed unit text that matches a code or alias
- **THEN** the system rejects the request and reports that the line requires a unit
- **AND** the report names which line it is
- **AND** no purchase is created

#### Scenario: A unit matched during extraction satisfies the rule

- **WHEN** a capture is confirmed whose candidate line carries no unit code but was matched to a unit while extracting
- **THEN** the expense is accepted with that matched unit

#### Scenario: Printed unit text alone satisfies the rule

- **WHEN** a purchase is recorded whose expense line supplies no unit code but carries the printed unit text "KOM", and "kom" is an alias of PCS
- **THEN** the expense is accepted with the unit PCS and the printed text "KOM"

#### Scenario: A supplied unit code overrides the printed text

- **WHEN** an expense line supplies the unit code PACK and carries the printed unit text "kom", which is an alias of PCS
- **THEN** the expense is recorded with the unit PACK

#### Scenario: A description at the limit is accepted

- **WHEN** an expense is recorded with a description of exactly 200 characters
- **THEN** the expense is accepted with that description stored whole

#### Scenario: An over-long description is rejected

- **WHEN** an expense is recorded with a description of 201 characters
- **THEN** the system rejects the request and reports that the description is too long
- **AND** no purchase is created
- **AND** nothing is stored truncated

#### Scenario: Existing unit-less expenses stay readable

- **WHEN** a purchase recorded before this rule holds an expense with no unit
- **THEN** reading that purchase returns the expense unchanged

## ADDED Requirements

### Requirement: Saving an expense line teaches a unit alias

When an expense line is saved carrying printed unit text and a unit, and the printed text in normalised form is neither an active or inactive unit's code nor an existing alias, the system SHALL record that normalised text as an alias of the line's unit. This SHALL apply however the line is saved: recorded directly, confirmed from a purchase's extraction candidates, or confirmed from a capture. The alias SHALL be recorded in the same operation as the expense, so that it exists if and only if the expense was saved. Learning SHALL NOT change an existing alias: where the printed text already maps to a different unit, the line SHALL keep the unit it was saved with, and the alias SHALL keep the unit it had. Where two lines of one save carry the same unknown text with different units, the earlier line SHALL decide the alias. Learning SHALL NOT occur for printed text that normalises to nothing, and SHALL NOT occur when no unit is saved.

No expense recorded before this rule SHALL be re-matched or altered by it.

#### Scenario: An unknown spelling is learned

- **WHEN** a capture is confirmed whose line carries the printed unit text "KD", which matches no code or alias, and the user chose the unit PCS for it
- **THEN** the expense is recorded with the unit PCS and the printed text "KD"
- **AND** "kd" is from then on an alias of PCS

#### Scenario: Learning applies to direct recording

- **WHEN** a purchase is recorded directly with a line carrying the unit code PACK and the printed unit text "kut", which matches no code or alias
- **THEN** "kut" becomes an alias of PACK

#### Scenario: A known alias is not overridden

- **WHEN** "kom" is an alias of PCS
- **AND** a line carrying the printed unit text "KOM" is saved with the unit PACK
- **THEN** the expense is recorded with the unit PACK
- **AND** "kom" is still an alias of PCS

#### Scenario: A matched unit teaches nothing

- **WHEN** a line whose printed unit text "KOM" matched PCS through its alias is confirmed with PCS
- **THEN** no alias is added or changed

#### Scenario: A code teaches nothing

- **WHEN** a line carrying the printed unit text "kg." is saved with the unit PCS
- **THEN** no alias "kg" is created, because "kg" is the code of the unit KG

#### Scenario: The earlier line decides within one save

- **WHEN** a purchase is saved whose first line carries "zz" with PCS and whose second line carries "zz" with PACK, and "zz" matched nothing
- **THEN** "zz" becomes an alias of PCS
- **AND** the second line is still recorded with PACK

#### Scenario: A rejected save teaches nothing

- **WHEN** a purchase carrying a line with an unknown printed unit text and a chosen unit is rejected because its lines do not reconcile
- **THEN** no alias is created

#### Scenario: History is not re-matched

- **WHEN** an expense was recorded before this change with the printed unit text "KD" and no unit
- **AND** the alias "kd" is then learned or added
- **THEN** that expense still carries no unit
