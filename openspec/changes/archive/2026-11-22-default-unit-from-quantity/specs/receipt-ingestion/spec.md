## MODIFIED Requirements

### Requirement: Extracted unit text is matched to a unit

After extraction produces candidate lines — whether from a captured image, from a fiscal QR payload alone, or from a re-run on a purchase's stored receipt — each line carrying printed unit text SHALL be matched to a unit. Matching SHALL compare the text in normalised form first against the codes of active units and then against the aliases of active units, and SHALL take the first match. A line whose text matches neither, or that carries no printed unit text, SHALL carry a unit inferred from its quantity: PCS where the quantity, taken as 1 when absent, is a whole number, and KG otherwise. A line given an inferred unit SHALL be marked as such, and a matched line SHALL NOT be. Where the unit to infer is not active, the line SHALL carry no unit. Matching and inference SHALL apply identically whichever extraction stage produced the line, and SHALL NOT alter the printed text the line carries.

#### Scenario: A fiscal invoice's units are matched

- **WHEN** the receipt with IKOF 32AA324CFF5030271E16D59F7F8EF636 is captured and the fiscal portal returns its fourteen lines, eight printed "KOM" and six printed "KG"
- **THEN** the eight "KOM" lines carry the unit PCS, through the alias "kom"
- **AND** the six "KG" lines carry the unit KG, through its code
- **AND** each line still carries the printed text exactly as returned
- **AND** no line is marked as carrying an inferred unit

#### Scenario: Vision-extracted units are matched

- **WHEN** the vision stage extracts a line whose unit reads "kom"
- **THEN** the candidate line carries the unit PCS

#### Scenario: A code match is preferred to an alias

- **WHEN** a line's printed unit text normalises to a unit's code
- **THEN** the line carries that unit, whatever aliases exist

#### Scenario: Unknown text leaves the line unmatched

- **WHEN** a line's printed unit reads "Pfand", no code or alias matches it, and its quantity is 2
- **THEN** the candidate line is matched to no unit, and carries the unit PCS marked as inferred
- **AND** it carries the printed text "Pfand"

#### Scenario: Unknown text with a fractional quantity is inferred as kilograms

- **WHEN** a line's printed unit reads "LIT", no code or alias matches it, and its quantity is 1.5
- **THEN** the candidate line carries the unit KG, marked as inferred
- **AND** it carries the printed text "LIT"

#### Scenario: A line with no printed unit is inferred

- **WHEN** a line carries no printed unit text and its quantity is 0.482
- **THEN** the candidate line carries the unit KG, marked as inferred

#### Scenario: An absent quantity is inferred as pieces

- **WHEN** a line carries no printed unit text and no quantity
- **THEN** the candidate line carries the unit PCS, marked as inferred

#### Scenario: A whole quantity written with decimals is a whole number

- **WHEN** a line carries no printed unit text and its quantity is 1.000
- **THEN** the candidate line carries the unit PCS, marked as inferred

#### Scenario: An inactive unit is not inferred

- **WHEN** the unit PCS has been deactivated
- **AND** a line's printed unit matches nothing and its quantity is 3
- **THEN** the candidate line carries no unit

#### Scenario: A re-run uses the aliases as they now stand

- **WHEN** a purchase's receipt was extracted while "kd" was not an alias, leaving its "KD" lines with an inferred unit
- **AND** the alias "kd" is added to PACK
- **AND** extraction is re-run for that receipt
- **THEN** the "KD" lines carry the unit PACK, not marked as inferred

#### Scenario: A receipt whose units all match can be confirmed unchanged

- **WHEN** every candidate line of a capture was matched to a unit
- **AND** the capture is confirmed with the candidate lines as extracted
- **THEN** the purchase is created without any unit being chosen by hand

#### Scenario: A receipt with inferred units can be confirmed unchanged

- **WHEN** a capture's candidate lines carry matched or inferred units
- **AND** the capture is confirmed with the candidate lines as extracted
- **THEN** the purchase is created with each line's matched or inferred unit
