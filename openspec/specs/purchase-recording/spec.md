# purchase-recording Specification

## Purpose

Defines how a purchase and the expense lines it contains are recorded, where a purchase was made, what a line cost before any discount, how their monetary amounts must reconcile, and how repeated submissions of the same purchase are absorbed without creating duplicate ledger entries.

## Requirements

### Requirement: Purchase is a container of expenses

A purchase SHALL represent a single act of paying, and SHALL contain one or more expenses. An expense SHALL NOT exist outside a purchase. A manually entered expense SHALL be recorded as a purchase containing exactly one expense.

#### Scenario: Manual single-line entry

- **WHEN** a user records "Lunch, 12.40, on 2026-08-19"
- **THEN** the system creates one purchase with amount 12.40 occurring on 2026-08-19
- **AND** that purchase contains exactly one expense described as "Lunch" with amount 12.40

#### Scenario: Itemised entry

- **WHEN** a user records a purchase of 80.00 with lines of 60.00, 18.50 and 1.50
- **THEN** the system creates one purchase with amount 80.00 containing three expenses

#### Scenario: Purchase with no expenses is rejected

- **WHEN** a purchase is submitted with an empty list of expenses
- **THEN** the system rejects the request and reports that at least one expense is required
- **AND** no purchase is created

### Requirement: Purchase amount reconciles with its expenses

The sum of the amounts of the expenses within a purchase SHALL equal the purchase amount exactly. The system SHALL reject any purchase where these disagree, and the rejection SHALL state both the purchase amount and the computed sum.

#### Scenario: Amounts reconcile

- **WHEN** a purchase of 80.00 is submitted with expenses of 60.00, 18.50 and 1.50
- **THEN** the purchase is accepted

#### Scenario: Amounts do not reconcile

- **WHEN** a purchase of 80.00 is submitted with expenses of 60.00 and 18.50
- **THEN** the system rejects the request
- **AND** the error states that the purchase amount is 80.00 while its expenses sum to 78.50

#### Scenario: Reconciliation is exact, not approximate

- **WHEN** a purchase of 10.00 is submitted with expenses of 3.33, 3.33 and 3.33
- **THEN** the system rejects the request because the expenses sum to 9.99

### Requirement: Monetary amounts are exact

All monetary amounts SHALL be stored and returned as exact decimal values with two decimal places of precision, and SHALL NOT be subject to binary floating-point rounding. Amounts SHALL be returned with the same precision they were accepted with.

#### Scenario: Precision is preserved

- **WHEN** an expense with unit price 1.7190 is recorded
- **THEN** reading that expense back returns unit price 1.7190

#### Scenario: Amounts cannot be negative

- **WHEN** a purchase or expense is submitted with a negative amount
- **THEN** the system rejects the request

### Requirement: Purchase time defaults to the start of the day

A purchase SHALL record the wall-clock date and time printed on the receipt or supplied by the user, without any timezone conversion. When the time of day is unknown, the system SHALL record 00:00:00 on the given date.

#### Scenario: Date and time supplied

- **WHEN** a purchase is recorded as occurring on 2026-08-19 at 14:03
- **THEN** the stored occurrence is 2026-08-19 14:03:00

#### Scenario: Only a date supplied

- **WHEN** a purchase is recorded as occurring on 2026-08-19 with no time
- **THEN** the stored occurrence is 2026-08-19 00:00:00

#### Scenario: No timezone conversion is applied

- **WHEN** a purchase occurring on 2026-08-19 at 00:00 is recorded from a client in any timezone
- **THEN** the stored occurrence is 2026-08-19 00:00:00 regardless of the timezone of the client
- **AND** the purchase appears in a report for 2026-08-19

### Requirement: Expense line detail

Each expense SHALL carry a description, a quantity, and an amount. Each expense MAY carry a unit, a unit price, and a category. Quantity SHALL default to 1 when not supplied and SHALL NOT be null. Quantity SHALL support fractional values to at least three decimal places.

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

### Requirement: Duplicate purchase submissions are absorbed

Adding a purchase whose occurrence and amount both match an existing purchase SHALL NOT create a second purchase. The system SHALL instead return the existing purchase as a successful outcome, distinguishable from a newly created one. This guard protects against repeated submission of the same request; it is not a semantic search for similar purchases.

#### Scenario: Same purchase submitted twice

- **WHEN** a purchase occurring 2026-08-19 14:03 for 12.40 is recorded
- **AND** an identical request is submitted again
- **THEN** the second request succeeds
- **AND** it returns the purchase created by the first request
- **AND** the result indicates that the purchase already existed
- **AND** exactly one purchase exists

#### Scenario: Concurrent duplicate submissions

- **WHEN** two identical purchase requests are submitted at the same time
- **THEN** exactly one purchase is created
- **AND** both requests succeed and return the same purchase

#### Scenario: Expenses of a duplicate submission are ignored

- **WHEN** a purchase occurring 2026-08-19 14:03 for 12.40 already exists with one expense
- **AND** a request with the same occurrence and amount but different expenses is submitted
- **THEN** the existing purchase is returned unchanged
- **AND** its expenses are not replaced or appended to

#### Scenario: Differing amount is not a duplicate

- **WHEN** a purchase occurring 2026-08-19 14:03 for 12.40 exists
- **AND** a purchase occurring 2026-08-19 14:03 for 12.50 is submitted
- **THEN** a second purchase is created

#### Scenario: Differing occurrence is not a duplicate

- **WHEN** a purchase occurring 2026-08-19 14:03 for 12.40 exists
- **AND** a purchase occurring 2026-08-19 15:20 for 12.40 is submitted
- **THEN** a second purchase is created

#### Scenario: Two identical same-day purchases with unknown times

- **WHEN** a purchase occurring 2026-08-19 with no time for 2.90 exists
- **AND** a second genuinely different purchase occurring 2026-08-19 with no time for 2.90 is submitted
- **THEN** the existing purchase is returned instead of a new one
- **AND** the user is able to record the second purchase by supplying a time of day, or by adding it as a further expense within the existing purchase

### Requirement: Purchases can be retrieved and listed

The system SHALL allow retrieving a single purchase together with its expenses, and listing purchases filtered by an occurrence date range. Listings SHALL be ordered by occurrence, most recent first, and SHALL be pageable.

#### Scenario: Retrieve a purchase

- **WHEN** a purchase is requested by its identifier
- **THEN** the purchase is returned with all of its expenses

#### Scenario: Retrieve a purchase that does not exist

- **WHEN** a purchase is requested by an identifier that does not exist
- **THEN** the system reports that it was not found

#### Scenario: List by date range

- **WHEN** purchases occurring between 2026-08-01 and 2026-08-31 are requested
- **THEN** only purchases whose occurrence falls within that range are returned
- **AND** they are ordered from most recent to oldest

### Requirement: A purchase records where it was made

A purchase MAY name the merchant it was made at. Where a merchant is named, the system SHALL retain the merchant text exactly as supplied or as printed on the receipt, in addition to any dictionary merchant it was matched to. Merchant text SHALL be retained whether or not a match was found, and a later match SHALL NOT erase it. A purchase without a merchant SHALL be valid.

#### Scenario: Purchase with a matched merchant

- **WHEN** a purchase is recorded naming a merchant that resolves to a known dictionary entry
- **THEN** the purchase reports that merchant
- **AND** the merchant text as supplied is also retained

#### Scenario: Purchase with an unmatched merchant

- **WHEN** a purchase is recorded naming "AROMA" and no dictionary merchant matches
- **THEN** the purchase is accepted
- **AND** the verbatim text "AROMA" is retained
- **AND** the purchase reports no dictionary merchant

#### Scenario: Matching later does not erase merchant text

- **WHEN** a purchase holding only verbatim merchant text is later matched to a dictionary merchant
- **THEN** the verbatim text is still retained

#### Scenario: Purchase with no merchant

- **WHEN** a purchase is recorded with no merchant
- **THEN** the purchase is accepted and reports no merchant

#### Scenario: Merchant does not affect the duplicate guard

- **WHEN** a purchase occurring 2026-08-24 12:50:08 for 8.48 at merchant A exists
- **AND** a purchase with the same occurrence and amount at merchant B is submitted
- **THEN** the existing purchase is returned unchanged
- **AND** its merchant is not replaced

### Requirement: An expense line records what it would have cost

An expense MAY carry a list unit price and a discount amount, recording what the item normally costs and how much was taken off. Both SHALL be optional, and SHALL be absent together when the source recorded no discount. A discount amount SHALL be a non-negative magnitude, never a negative monetary value. Neither value SHALL participate in reconciliation, and the system SHALL NOT recompute the expense amount from them.

#### Scenario: Discounted line

- **WHEN** an expense is recorded with amount 4.49, list unit price 8.50 and discount amount 4.01
- **THEN** all three values are retained and returned unchanged

#### Scenario: Discount does not affect reconciliation

- **WHEN** a purchase of 8.48 is submitted with expenses of 4.49 and 3.99, both carrying discounts
- **THEN** the purchase is accepted
- **AND** reconciliation considers only the amounts 4.49 and 3.99

#### Scenario: Amount is not recomputed from list price and discount

- **WHEN** an expense is recorded with amount 4.49, list unit price 8.50 and discount amount 4.00
- **THEN** the expense is accepted with amount 4.49
- **AND** the amount is not adjusted to 4.50

#### Scenario: Discount percentage is derived, not stored

- **WHEN** an expense with list unit price 8.50 and discount amount 4.01 is retrieved
- **THEN** a discount percentage of 47.18 is reported
- **AND** submitting a discount percentage in place of a discount amount is rejected

#### Scenario: Absence of a discount is distinguishable from a zero discount

- **WHEN** one expense is recorded with no discount and another with a discount amount of 0.00
- **THEN** the first reports no discount
- **AND** the second reports a discount of 0.00

#### Scenario: Negative discount is rejected

- **WHEN** an expense is submitted with a discount amount of -4.01
- **THEN** the system rejects the request

#### Scenario: Savings are reported for a purchase

- **WHEN** a purchase containing expenses with discounts of 4.01 and 3.51 is retrieved
- **THEN** the purchase reports a total saving of 7.52
