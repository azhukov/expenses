# ledger-export Specification

## Purpose

Defines what an export of the ledger contains and how it is encoded: the range it covers, one row per expense line with its purchase's identity, how merchants, categories and units are named, the order rows come in, and the fixed CSV dialect that lets any spreadsheet or script read it the same way.

## Requirements

### Requirement: The ledger can be exported for a chosen range

The system SHALL export the purchases whose occurrence falls in an inclusive date range, and SHALL export every purchase when no range is given. Either bound MAY be given alone. A purchase late on the last day of the range SHALL be included. A range whose end falls before its start SHALL be rejected, with the same error the listing reports. Exporting SHALL NOT change the ledger.

#### Scenario: Export everything

- **WHEN** the ledger holds purchases in August, September and October 2026 and an export is requested without a range
- **THEN** the export contains the lines of every purchase

#### Scenario: Export one month

- **WHEN** an export is requested from 2026-10-01 to 2026-10-31
- **THEN** the export contains the lines of every purchase occurring in October 2026
- **AND** contains no line of a purchase from September or November

#### Scenario: The last day is included whole

- **WHEN** a purchase occurred at 2026-10-31 23:59:30 and an export is requested to 2026-10-31
- **THEN** that purchase's lines are in the export

#### Scenario: Only a start is given

- **WHEN** an export is requested from 2026-10-15 with no end
- **THEN** the export contains every purchase occurring on or after 2026-10-15

#### Scenario: An inverted range is rejected

- **WHEN** an export is requested from 2026-10-31 to 2026-10-01
- **THEN** the request is rejected with the error the listing gives for an inverted range
- **AND** no file is produced

#### Scenario: Nothing in the range

- **WHEN** an export is requested for a range in which no purchase occurred
- **THEN** the export succeeds and contains the header row only

#### Scenario: An export is not paged

- **WHEN** the range holds more purchases than the largest listing page
- **THEN** the export contains the lines of every one of them

### Requirement: An export has one row per expense line

An export SHALL contain one row per expense line, in this column order: `purchase_id`, `occurred_at`, `merchant`, `description`, `quantity`, `unit`, `unit_price`, `list_unit_price`, `discount_amount`, `category`, `amount`. The first row SHALL be those column names. The purchase total SHALL NOT be repeated per line, so summing `amount` over any export gives the total of the purchases it covers.

#### Scenario: A purchase with three lines

- **WHEN** a purchase of 12.40 with three expense lines is exported
- **THEN** the export contains three rows carrying that purchase's identifier
- **AND** their `amount` values sum to 12.40

#### Scenario: The header row

- **WHEN** any export is produced
- **THEN** its first row is exactly `purchase_id,occurred_at,merchant,description,quantity,unit,unit_price,list_unit_price,discount_amount,category,amount`

#### Scenario: Optional values that are absent

- **WHEN** a line carries no unit price, no discount and no category
- **THEN** the `unit_price`, `list_unit_price`, `discount_amount` and `category` fields of its row are empty

### Requirement: An export names references rather than identifying them

An export SHALL give a line's merchant, category and unit by name. The merchant is the purchase's matched merchant's name, the category is the matched category's name, and the unit is the matched unit's symbol. A deactivated reference SHALL still be named. Where a reference was never matched, the verbatim text that was recorded SHALL be exported instead. Where there is neither, the field SHALL be empty. No reference identifier SHALL appear.

#### Scenario: Matched references are named

- **WHEN** a line's purchase is matched to the merchant "Voli" and the line is matched to the category "Groceries" and the unit kilogram
- **THEN** its row carries `Voli`, `Groceries` and `kg`

#### Scenario: Unmatched references fall back to what was printed

- **WHEN** a purchase carries the verbatim merchant text "VOLI TRADE DOO" but no matched merchant
- **THEN** each of its rows carries `VOLI TRADE DOO` as the merchant

#### Scenario: A deactivated merchant is still named

- **WHEN** a purchase is matched to a merchant that has since been deactivated
- **THEN** its rows carry that merchant's name

#### Scenario: Neither a reference nor verbatim text

- **WHEN** a line has no category and no verbatim category text
- **THEN** its `category` field is empty

### Requirement: An export is ordered as a spreadsheet reads

An export SHALL order purchases by occurrence from oldest to most recent. Purchases occurring at the same moment SHALL be ordered by the order they were recorded in. Lines within a purchase SHALL keep the order in which they were recorded, and SHALL be adjacent.

#### Scenario: Oldest first

- **WHEN** purchases occurred on 2026-10-03, 2026-10-01 and 2026-10-02 and they are exported
- **THEN** the rows of the 2026-10-01 purchase come first and those of the 2026-10-03 purchase last

#### Scenario: Lines keep their recorded order

- **WHEN** a purchase was recorded with the lines "Bread", "Milk" and "Eggs" in that order
- **THEN** its rows appear in that order, one after another

### Requirement: An export is encoded in a fixed CSV dialect

An export SHALL be UTF-8 text beginning with a byte-order mark, with comma-separated fields and CRLF row endings. A field containing a comma, a double quote or a line break SHALL be enclosed in double quotes, with inner quotes doubled. Numbers SHALL use `.` as the decimal separator and no grouping. Money SHALL carry two decimals. Quantities SHALL carry no trailing zeros. Occurrence SHALL be written as `yyyy-MM-dd HH:mm:ss`, as recorded, with no timezone conversion.

#### Scenario: Text with commas and quotes survives

- **WHEN** a line's description is `Juice "Orange", 1L`
- **THEN** its field is written as `"Juice ""Orange"", 1L"`
- **AND** reading the file back with any RFC 4180 reader yields the original description

#### Scenario: Non-Latin text survives

- **WHEN** a line's description is `Hljeb, čokolada, Ćevapi`
- **THEN** the file begins with a UTF-8 byte-order mark
- **AND** the description reads back unchanged

#### Scenario: Numbers use an invariant form

- **WHEN** a line has quantity 1.25, unit price 3.2 and amount 4
- **THEN** its fields are written as `1.25`, `3.20` and `4.00`

#### Scenario: Quantities carry no trailing zeros

- **WHEN** lines with quantities 1, 2.5 and 0.375 are exported
- **THEN** their quantity fields are written as `1`, `2.5` and `0.375`, not `1.000`, `2.500` and `0.375`

#### Scenario: Occurrence is written as recorded

- **WHEN** a purchase occurred at 2026-10-14 13:05:00 local wall-clock time
- **THEN** its rows carry `2026-10-14 13:05:00` regardless of the server's timezone

#### Scenario: A purchase recorded by date alone

- **WHEN** a purchase was recorded with a date and no time of day
- **THEN** its rows carry that date with `00:00:00`
