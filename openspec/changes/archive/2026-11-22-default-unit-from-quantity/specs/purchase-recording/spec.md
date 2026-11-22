## MODIFIED Requirements

### Requirement: Saving an expense line teaches a unit alias

When an expense line is saved carrying printed unit text and a unit, and the printed text in normalised form is neither an active or inactive unit's code nor an existing alias, the system SHALL record that normalised text as an alias of the line's unit. This SHALL apply however the line is saved: recorded directly, confirmed from a purchase's extraction candidates, or confirmed from a capture. The alias SHALL be recorded in the same operation as the expense, so that it exists if and only if the expense was saved. Learning SHALL NOT change an existing alias: where the printed text already maps to a different unit, the line SHALL keep the unit it was saved with, and the alias SHALL keep the unit it had. Where two lines of one save carry the same unknown text with different units, the earlier line SHALL decide the alias. Learning SHALL NOT occur for printed text that normalises to nothing, SHALL NOT occur when no unit is saved, and SHALL NOT occur for a line saved with an inferred unit left as inferred. A caller marks a line's unit as inferred; doing so changes only whether the line teaches an alias, never which unit it is saved with or whether it is accepted.

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

#### Scenario: An inferred unit confirmed untouched teaches nothing

- **WHEN** a capture is confirmed whose line carries the printed unit text "Pfand", which matches no code or alias, with the unit PCS inferred from its quantity and left as inferred
- **THEN** the expense is recorded with the unit PCS and the printed text "Pfand"
- **AND** no alias "pfand" is created

#### Scenario: An inferred unit confirmed unedited from candidates teaches nothing

- **WHEN** a purchase's extraction candidates are confirmed without edited lines, and a line carries the printed unit text "LIT" with the inferred unit KG
- **THEN** the expense is recorded with the unit KG
- **AND** no alias "lit" is created

#### Scenario: A unit chosen in place of an inferred one is learned

- **WHEN** a capture is confirmed whose line carries the printed unit text "LIT", which matches no code or alias, and the user replaced the inferred unit KG with L
- **THEN** the expense is recorded with the unit L
- **AND** "lit" is from then on an alias of L

#### Scenario: Marking a unit as inferred does not supply one

- **WHEN** a purchase is recorded directly with a line marked as carrying an inferred unit but supplying no unit code and no printed text that matches
- **THEN** the system rejects the request and reports that the line requires a unit
