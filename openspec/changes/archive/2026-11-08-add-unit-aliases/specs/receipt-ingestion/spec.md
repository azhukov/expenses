## ADDED Requirements

### Requirement: Extracted unit text is matched to a unit

After extraction produces candidate lines — whether from a captured image, from a fiscal QR payload alone, or from a re-run on a purchase's stored receipt — each line carrying printed unit text SHALL be matched to a unit. Matching SHALL compare the text in normalised form first against the codes of active units and then against the aliases of active units, and SHALL take the first match. A line whose text matches neither SHALL carry no unit. Matching SHALL apply identically whichever extraction stage produced the line, and SHALL NOT alter the printed text the line carries.

#### Scenario: A fiscal invoice's units are matched

- **WHEN** the receipt with IKOF 32AA324CFF5030271E16D59F7F8EF636 is captured and the fiscal portal returns its fourteen lines, eight printed "KOM" and six printed "KG"
- **THEN** the eight "KOM" lines carry the unit PCS, through the alias "kom"
- **AND** the six "KG" lines carry the unit KG, through its code
- **AND** each line still carries the printed text exactly as returned

#### Scenario: Vision-extracted units are matched

- **WHEN** the vision stage extracts a line whose unit reads "kom"
- **THEN** the candidate line carries the unit PCS

#### Scenario: A code match is preferred to an alias

- **WHEN** a line's printed unit text normalises to a unit's code
- **THEN** the line carries that unit, whatever aliases exist

#### Scenario: Unknown text leaves the line unmatched

- **WHEN** a line's printed unit reads "Pfand" and no code or alias matches it
- **THEN** the candidate line carries no unit
- **AND** it carries the printed text "Pfand"

#### Scenario: A re-run uses the aliases as they now stand

- **WHEN** a purchase's receipt was extracted while "kd" was not an alias, leaving its "KD" lines unmatched
- **AND** the alias "kd" is added to PCS
- **AND** extraction is re-run for that receipt
- **THEN** the "KD" lines carry the unit PCS

#### Scenario: A receipt whose units all match can be confirmed unchanged

- **WHEN** every candidate line of a capture was matched to a unit
- **AND** the capture is confirmed with the candidate lines as extracted
- **THEN** the purchase is created without any unit being chosen by hand
