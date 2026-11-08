## ADDED Requirements

### Requirement: Applying migrations on startup is a setting

The HTTP host SHALL apply pending database migrations when it starts if, and only if, it runs in the `Development` environment or its configuration explicitly turns migrating on startup on. In every other case it SHALL apply none. In every case it SHALL still verify the database's provisioning before it starts answering requests, whether or not it migrated.

#### Scenario: Development migrates by default

- **WHEN** the host starts in the `Development` environment with no migration setting
- **THEN** pending migrations are applied before it answers requests

#### Scenario: Other environments do not migrate by default

- **WHEN** the host starts in an environment other than `Development` with no migration setting
- **THEN** no migration is applied

#### Scenario: The setting turns migrating on

- **WHEN** the host starts in an environment other than `Development` with migrating on startup turned on
- **THEN** pending migrations are applied before it answers requests

#### Scenario: Provisioning is verified either way

- **WHEN** the host starts against a database created without the required collation
- **THEN** it does not start, whether or not migrating on startup is on

### Requirement: The host has a Railway environment

The HTTP host SHALL define a `Railway` environment. When it starts in that environment, it SHALL allow `https://expenses-frontend-production-2e52.up.railway.app` as its only browser origin, and it SHALL apply pending migrations on startup. All connection details and secrets SHALL come from the environment the host is started with, and SHALL NOT be committed in that environment's configuration.

#### Scenario: The Railway client's origin reads the ledger

- **WHEN** the host runs in the `Railway` environment
- **AND** a request from `https://expenses-frontend-production-2e52.up.railway.app` lists categories
- **THEN** the response allows that origin

#### Scenario: Local development origins are not allowed on Railway

- **WHEN** the host runs in the `Railway` environment
- **AND** a request from `http://localhost:5173` lists categories
- **THEN** the response does not allow that origin

#### Scenario: Railway migrates on startup

- **WHEN** the host starts in the `Railway` environment against a database with pending migrations
- **THEN** the migrations are applied before it answers requests
