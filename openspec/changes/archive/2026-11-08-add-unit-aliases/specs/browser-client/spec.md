## ADDED Requirements

### Requirement: Unit aliases can be managed from the client

The client SHALL provide a units screen, reachable from the home screen, that lists every active unit by display name and symbol together with its aliases. From that screen the user SHALL be able to add an alias to a unit and remove an alias from a unit. A rejected change SHALL be reported beside the unit it concerns, in terms of the alias submitted, and SHALL leave the listing as it was. The screen SHALL show the aliases as the ledger now holds them after every change, including aliases learned while confirming purchases.

#### Scenario: The units screen lists aliases

- **WHEN** the user opens the units screen and the unit PCS has the aliases "kom" and "kd"
- **THEN** PCS is shown with its display name, its symbol and both aliases

#### Scenario: The units screen is reachable from home

- **WHEN** the user is on the home screen
- **THEN** a control leads to the units screen

#### Scenario: Adding an alias

- **WHEN** the user adds the alias "kut" to the unit PACK
- **THEN** PACK is shown with "kut" among its aliases

#### Scenario: Removing an alias

- **WHEN** the user removes the alias "kd" from the unit PCS
- **THEN** PCS is no longer shown with "kd"

#### Scenario: A conflicting alias is reported in place

- **WHEN** the user adds "kom" to PACK while "kom" maps to PCS
- **THEN** the screen reports, beside PACK, that "kom" already belongs to Piece
- **AND** neither unit's aliases change

#### Scenario: A learned alias appears

- **WHEN** a purchase is confirmed that teaches the alias "kd" for PCS
- **AND** the user then opens the units screen
- **THEN** PCS is shown with "kd"

#### Scenario: Aliases can be read when they cannot be changed

- **WHEN** the unit listing can be read but adding an alias fails because the ledger cannot be reached
- **THEN** the listing is still shown
- **AND** the failure is reported without discarding what the user typed
