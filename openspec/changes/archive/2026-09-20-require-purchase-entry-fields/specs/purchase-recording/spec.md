## MODIFIED Requirements

### Requirement: Expense line detail

Each expense SHALL carry a description, a quantity, an amount and a unit. A description SHALL be at most 200 characters after surrounding whitespace is removed, and an expense whose description exceeds that SHALL be rejected rather than truncated. A unit SHALL be established either by the unit code the caller supplies or by a unit matched while extracting the line; an expense that establishes neither SHALL be rejected, and the rejection SHALL name the line at fault. Each expense MAY carry a unit price and a category. Quantity SHALL default to 1 when not supplied and SHALL NOT be null. Quantity SHALL support fractional values to at least three decimal places.

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

- **WHEN** a purchase is recorded whose expense line supplies no unit code and was matched to no unit
- **THEN** the system rejects the request and reports that the line requires a unit
- **AND** the report names which line it is
- **AND** no purchase is created

#### Scenario: A unit matched during extraction satisfies the rule

- **WHEN** a capture is confirmed whose candidate line carries no unit code but was matched to a unit while extracting
- **THEN** the expense is accepted with that matched unit

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
