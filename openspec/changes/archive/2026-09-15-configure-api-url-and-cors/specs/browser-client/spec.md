## ADDED Requirements

### Requirement: The ledger address is configured when the client is served

The client SHALL reach the ledger at an address taken from the environment of the server that serves the client, read when that server starts. The address SHALL NOT be fixed into the built client: serving the same build with a different address in the environment SHALL send every request to the new address without rebuilding. Every read, write and upload SHALL be sent to that address, and none SHALL rely on the server that serves the client forwarding requests to the ledger. Where the address is absent, or is not an absolute `http` or `https` URL, the serving server SHALL refuse to start and SHALL name the missing or malformed setting, rather than serving a client that cannot reach the ledger. An address given with or without a trailing slash SHALL produce the same request URLs.

#### Scenario: Requests go to the configured address

- **WHEN** the client is served with the ledger address set to `http://ledger.test:9000`
- **AND** the home screen loads purchases
- **THEN** the request is sent to `http://ledger.test:9000/purchases` with the month's query
- **AND** no request for ledger data is sent to the origin that served the client

#### Scenario: Uploads go to the configured address

- **WHEN** a captured image is uploaded
- **THEN** the upload is sent to the capture endpoint under the configured address

#### Scenario: A different address needs no rebuild

- **WHEN** a built client is served once with one ledger address and again with another
- **THEN** each time its requests go to the address the server was started with

#### Scenario: A trailing slash does not change the requests

- **WHEN** the address is given as `http://ledger.test:9000/`
- **THEN** a request for merchants is sent to `http://ledger.test:9000/merchants`, with no doubled slash

#### Scenario: A missing address stops the server

- **WHEN** the development or preview server is started with no ledger address in its environment
- **THEN** it does not start
- **AND** its output names the setting that is missing

#### Scenario: A malformed address stops the server

- **WHEN** the server is started with a ledger address that is not an absolute `http` or `https` URL
- **THEN** it does not start
- **AND** its output names the setting and says why the value was refused

#### Scenario: Ledger errors are still shown cross-origin

- **WHEN** the client is served from a different origin than the ledger's
- **AND** a request fails with an error carrying a human-readable message
- **THEN** that message is what the user is shown, not a report that the ledger could not be reached
