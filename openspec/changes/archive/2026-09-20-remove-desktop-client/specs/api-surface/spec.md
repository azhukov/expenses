## MODIFIED Requirements

### Requirement: Browser origins are allowed by configuration

The HTTP interface SHALL accept cross-origin browser requests only from origins listed in its configuration, and SHALL match those origins exactly (scheme, host and port). Where no origins are configured, it SHALL allow no cross-origin browser request. It SHALL answer preflight requests from an allowed origin for every method and request header the interface uses, and SHALL NOT allow credentials. Responses to an allowed origin SHALL carry the cross-origin headers whatever their status, so an error in the interface's error shape is as readable to a browser client as a success. Where a configured origin is not a plain `http` or `https` origin (a wildcard, a value with a path, query or fragment, or any other scheme), the host SHALL refuse to start and SHALL name the offending value. Requests without an `Origin` header, such as those from the MCP host or a command-line tool, SHALL be unaffected.

#### Scenario: An allowed origin reads the ledger

- **WHEN** `http://localhost:5173` is configured as an allowed origin
- **AND** a request from that origin lists categories
- **THEN** the response allows `http://localhost:5173` as its origin

#### Scenario: An unlisted origin is not allowed

- **WHEN** a request from `http://elsewhere.test` lists categories
- **AND** that origin is not configured
- **THEN** the response does not allow that origin

#### Scenario: Origins match exactly

- **WHEN** `http://localhost:5173` is configured
- **AND** a request comes from `https://localhost:5173` or `http://localhost:4173`
- **THEN** the response does not allow that origin

#### Scenario: Nothing configured allows nothing

- **WHEN** no origins are configured
- **AND** a request from any origin lists categories
- **THEN** the response does not allow that origin

#### Scenario: A preflight for a write is answered

- **WHEN** an allowed origin sends a preflight for a `POST` with a JSON content type
- **THEN** the preflight response allows that origin, that method and that header

#### Scenario: Methods beyond POST are allowed

- **WHEN** an allowed origin sends a preflight for a `PUT` or a `DELETE`
- **THEN** the preflight response allows that method

#### Scenario: An error response is readable cross-origin

- **WHEN** a request from an allowed origin fails validation, names a resource that does not exist, or fails unexpectedly
- **THEN** the response carries the error shape
- **AND** it allows that origin

#### Scenario: Credentials are not allowed

- **WHEN** an allowed origin sends a request
- **THEN** the response does not allow credentials

#### Scenario: A wildcard origin stops the host

- **WHEN** the host is started with `*` configured as an allowed origin
- **THEN** it does not start
- **AND** the failure names the offending value

#### Scenario: An origin with a path stops the host

- **WHEN** the host is started with `http://localhost:5173/app` configured as an allowed origin
- **THEN** it does not start
- **AND** the failure names the offending value

#### Scenario: Requests without an origin are unaffected

- **WHEN** a request with no `Origin` header lists categories
- **THEN** it succeeds exactly as it did before cross-origin access was configured
