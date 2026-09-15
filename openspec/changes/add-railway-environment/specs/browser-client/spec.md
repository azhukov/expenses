## ADDED Requirements

### Requirement: The preview server answers the host names it is served under

The server that serves the built client SHALL answer requests addressed to `localhost` and to every host name listed in its environment when it starts. It SHALL refuse requests addressed to any other host name. The list SHALL accept several names separated by commas, and SHALL ignore surrounding whitespace and empty entries. An entry that is not a bare host name (for example, one with a scheme, a path or a port) SHALL stop the server, with output that names the setting and the refused value.

#### Scenario: A listed public host is served

- **WHEN** the preview server starts with `expenses-frontend-production-2e52.up.railway.app` listed as a host name
- **AND** a request for the client arrives addressed to that host
- **THEN** the client is served
- **AND** `/config.js` carries the ledger address from the server's environment

#### Scenario: An unlisted host is refused

- **WHEN** the preview server starts with `expenses-frontend-production-2e52.up.railway.app` listed
- **AND** a request arrives addressed to `elsewhere.test`
- **THEN** the client is not served

#### Scenario: Nothing listed still serves localhost

- **WHEN** the preview server starts with no host names listed
- **AND** a request arrives addressed to `localhost`
- **THEN** the client is served

#### Scenario: Several hosts are listed

- **WHEN** the setting is ` a.example , b.example,`
- **THEN** both `a.example` and `b.example` are answered

#### Scenario: A URL instead of a host name stops the server

- **WHEN** the preview server is started with `https://expenses-frontend-production-2e52.up.railway.app/` listed
- **THEN** it does not start
- **AND** its output names the setting and the refused value
