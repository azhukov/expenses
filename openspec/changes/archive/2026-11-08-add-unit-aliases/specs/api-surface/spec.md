## ADDED Requirements

### Requirement: Unit aliases are available over both interfaces

Both interfaces SHALL list units with their aliases, add an alias to a unit, and remove an alias from a unit, with the same effect and the same validation outcome. Units SHALL be addressed by code and aliases by their text; neither interface SHALL require an alias identifier. Adding an alias that already maps to the same unit SHALL be reported as a success, so that repeating the call is safe. Rejections SHALL use the existing error shape and SHALL name the offending alias and, where the alias already maps elsewhere, the unit it maps to.

#### Scenario: Listing units over HTTP carries aliases

- **WHEN** the unit listing is requested over HTTP
- **THEN** each unit is returned with its aliases

#### Scenario: Listing units over MCP carries aliases

- **WHEN** the list-units tool is called
- **THEN** each unit is returned with its aliases

#### Scenario: Adding an alias over HTTP

- **WHEN** the alias "kut" is submitted over HTTP for the unit code PACK
- **THEN** the response reports success and carries the unit PACK with its aliases, including "kut"

#### Scenario: Adding an alias over MCP

- **WHEN** the add-unit-alias tool is called with the unit code PACK and the alias "kut"
- **THEN** the tool returns the unit PACK with its aliases, including "kut"

#### Scenario: Repeating an add is safe

- **WHEN** the add-unit-alias tool is called twice with the unit code PCS and the alias "kom"
- **THEN** both calls succeed
- **AND** "kom" maps to PCS once

#### Scenario: A conflicting alias is rejected the same way on both interfaces

- **WHEN** "kom" maps to PCS and a request over either interface adds "kom" to PACK
- **THEN** both interfaces reject it with the same stable error code
- **AND** the error names the alias "kom" and the unit PCS

#### Scenario: Removing an alias over HTTP

- **WHEN** removal of the alias "kd" from the unit code PCS is requested over HTTP
- **THEN** the response reports success
- **AND** "kd" no longer maps to any unit

#### Scenario: Removing an alias over MCP

- **WHEN** the remove-unit-alias tool is called with the unit code PCS and the alias "kd"
- **THEN** the tool reports success
- **AND** "kd" no longer maps to any unit

#### Scenario: Aliases are addressed in normalised form

- **WHEN** removal of the alias "KD." from the unit code PCS is requested over either interface
- **THEN** the alias "kd" is removed

#### Scenario: Units cannot be created through alias operations

- **WHEN** an alias is submitted for a unit code that does not exist
- **THEN** the request is rejected as unit not found
- **AND** no unit is created
