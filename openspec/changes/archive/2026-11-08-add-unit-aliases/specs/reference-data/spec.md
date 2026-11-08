## MODIFIED Requirements

### Requirement: Units describe what a quantity counts

Every unit SHALL have a code, a display name, a symbol, and a kind indicating whether it measures mass, volume or discrete count. Units SHALL be fixed reference data and SHALL NOT be created by the user. The spellings by which a receipt may print a unit are not part of the unit itself: they are held as the unit's aliases, which are editable while the unit is not.

#### Scenario: Unit detail

- **WHEN** the unit KG is retrieved
- **THEN** it returns the symbol "kg" and the kind mass

#### Scenario: User cannot create units

- **WHEN** a request attempts to create a new unit
- **THEN** the system rejects the request

#### Scenario: Adding an alias does not change the unit

- **WHEN** the alias "kom" is added to the unit PCS
- **THEN** the unit PCS keeps its code, display name, symbol and kind

### Requirement: Dictionaries can be listed

The system SHALL allow listing categories and units. Listings SHALL by default include only active entries, SHALL be able to include inactive entries on request, and SHALL return categories in a defined display order. Each listed unit SHALL carry its aliases.

#### Scenario: Default listing

- **WHEN** categories are listed without options
- **THEN** only active categories are returned

#### Scenario: Including inactive entries

- **WHEN** categories are listed with inactive entries requested
- **THEN** both active and inactive categories are returned, each marked with its state

#### Scenario: Display order is stable

- **WHEN** categories are listed twice with no intervening change
- **THEN** both listings return the same entries in the same order

#### Scenario: Units are listed with their aliases

- **WHEN** units are listed and the unit PCS has the aliases "kom" and "kd"
- **THEN** the listed unit PCS carries both aliases
- **AND** a unit with no aliases is listed with none

## ADDED Requirements

### Requirement: Unit aliases map printed text to a unit

The system SHALL keep a dictionary of unit aliases, each mapping one spelling a receipt may print to exactly one unit. A unit MAY have any number of aliases. An alias SHALL be unique across all units: one spelling SHALL NOT map to two units. An alias SHALL NOT equal the code of any unit, because a unit's code is matched on its own.

#### Scenario: One alias, one unit

- **WHEN** the alias "kom" maps to the unit PCS
- **AND** a request attempts to add "kom" to the unit PACK
- **THEN** the system rejects the request and names PCS as the unit "kom" already maps to

#### Scenario: An alias may not repeat a unit code

- **WHEN** a request attempts to add the alias "kg" to any unit
- **THEN** the system rejects the request and reports that "kg" is the code of the unit KG

#### Scenario: A unit with several aliases

- **WHEN** the aliases "kom", "kd" and "stk" are added to the unit PCS
- **THEN** each of the three maps to PCS

### Requirement: Aliases are compared in normalised form

Printed unit text and aliases SHALL be compared in one normalised form, applied identically whenever an alias is stored, looked up or removed, and whenever printed text is matched against unit codes. Normalisation SHALL ignore letter case, SHALL ignore surrounding whitespace, SHALL treat any run of internal whitespace as a single space, and SHALL ignore periods and slashes at either end. Characters in the middle of the text, including digits, SHALL be kept. An alias SHALL be stored in its normalised form. Text that normalises to nothing SHALL NOT be an alias and SHALL NOT match any unit.

#### Scenario: Spellings that differ only in form are one alias

- **WHEN** the alias "kom" maps to the unit PCS
- **THEN** the printed texts "KOM", " kom ", "kom." and "/kom/" each match PCS

#### Scenario: The alias is stored normalised

- **WHEN** the alias "  KOM. " is added to the unit PCS
- **THEN** the unit PCS is listed with the alias "kom"

#### Scenario: Inner characters are significant

- **WHEN** the alias "kom" maps to the unit PCS
- **THEN** the printed text "100kom" does not match PCS

#### Scenario: Text that normalises to nothing

- **WHEN** a request attempts to add the alias " ./ " to any unit
- **THEN** the system rejects the request and reports that the alias is empty

#### Scenario: Unit codes match in normalised form

- **WHEN** the printed text "kg." is matched
- **THEN** it matches the unit KG by its code, without any alias

### Requirement: A starting set of unit aliases is provided once

The system SHALL provide a starting set of aliases when the alias dictionary is first created: "kom", "kd", "kos", "stk", "st" and "pc" for PCS; "kgr" for KG; "gr" for G; "lit" and "ltr" for L; "pak" and "pkg" for PACK. The starting set SHALL be provided once and SHALL NOT be re-applied: an alias the user removes SHALL stay removed, and an alias the user adds SHALL be kept.

#### Scenario: Starting aliases are present

- **WHEN** the system starts against a database whose alias dictionary has just been created
- **THEN** the alias "kom" maps to PCS and the alias "kgr" maps to KG

#### Scenario: A removed starting alias stays removed

- **WHEN** the user removes the alias "st" and the system is restarted
- **THEN** "st" maps to no unit

#### Scenario: A user alias survives restart

- **WHEN** the user adds the alias "kut" to PACK and the system is restarted
- **THEN** "kut" still maps to PACK

### Requirement: Unit aliases can be added and removed

A user SHALL be able to add an alias to a unit and to remove an alias from a unit. Adding an alias that already maps to the same unit SHALL succeed and change nothing. Adding an alias to a deactivated unit SHALL be rejected. Removing an alias SHALL NOT change any expense already recorded, including one whose unit was matched through that alias. Removing an alias that does not map to the named unit SHALL be rejected as not found.

#### Scenario: Adding an alias

- **WHEN** the alias "kut" is added to the unit PACK
- **THEN** the unit PACK is listed with the alias "kut"
- **AND** printed text "KUT" is matched to PACK from then on

#### Scenario: Adding an alias the unit already has

- **WHEN** the alias "kom" maps to PCS and "kom" is added to PCS again
- **THEN** the request succeeds
- **AND** "kom" still maps to PCS exactly once

#### Scenario: Adding an alias to an unknown unit

- **WHEN** an alias is added to a unit code that does not exist
- **THEN** the system rejects the request and reports that the unit was not found

#### Scenario: Adding an alias to a deactivated unit

- **WHEN** an alias is added to a unit that has been deactivated
- **THEN** the system rejects the request and reports that the unit is inactive

#### Scenario: Removing an alias

- **WHEN** the alias "kd" is removed from the unit PCS
- **THEN** the unit PCS is no longer listed with "kd"
- **AND** printed text "kd" no longer matches any unit

#### Scenario: Removing an alias leaves history unchanged

- **WHEN** an expense was recorded with its unit matched through the alias "kd"
- **AND** the alias "kd" is then removed
- **THEN** the expense still reports the unit PCS and the printed text "kd"

#### Scenario: Removing an alias from the wrong unit

- **WHEN** the alias "kom" maps to PCS and a request removes "kom" from PACK
- **THEN** the system rejects the request and reports that PACK has no such alias
- **AND** "kom" still maps to PCS

### Requirement: Inactive units are not matched

Printed unit text SHALL NOT be matched to a deactivated unit, either by that unit's code or by any of its aliases. The aliases of a deactivated unit SHALL be retained.

#### Scenario: An alias of an inactive unit does not match

- **WHEN** the alias "pak" maps to the unit PACK and PACK is deactivated
- **THEN** printed text "pak" matches no unit
- **AND** the alias "pak" is still listed on PACK when inactive units are included
