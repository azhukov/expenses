## Purpose

Defines how the Api and Mcp hosts record what they do — where log output goes, how long it is
kept, and the guarantee that the MCP stdio transport's protocol stream is never polluted by it.

## ADDED Requirements

### Requirement: Console log output
Each host SHALL write leveled, human-readable log output to a console stream at startup and
throughout its lifetime, respecting the minimum log level and per-category overrides configured for
that host.

#### Scenario: Api host console output
- **WHEN** the `Expenses.Api` host starts
- **THEN** it writes its log output to stdout

#### Scenario: Mcp http-transport console output
- **WHEN** the `Expenses.Mcp` host starts with the `http` transport
- **THEN** it writes its log output to stdout

#### Scenario: Mcp stdio-transport console output stays off stdout
- **WHEN** the `Expenses.Mcp` host starts with the `stdio` transport (the default)
- **THEN** it writes its log output to stderr and writes nothing but MCP protocol traffic to stdout

### Requirement: Durable file log output
Each host SHALL persist its log output to a log file on disk in addition to its console output, so
that a record of what happened survives after the console is closed.

#### Scenario: Log file created on startup
- **WHEN** either host starts
- **THEN** a log file for the current day is created (or appended to, if already present) under a
  `logs/` directory relative to the host's content root

#### Scenario: Daily rotation
- **WHEN** a host is still running when the day rolls over
- **THEN** subsequent log entries are written to a new file for the new day, and the previous day's
  file is left unmodified

#### Scenario: Old log files are cleaned up
- **WHEN** a host starts and finds log files older than 31 days in the `logs/` directory
- **THEN** those older files are removed, keeping approximately the most recent 31 daily files

### Requirement: Startup and unhandled-exception logging
Each host SHALL log a message when it starts, and SHALL log any exception that would otherwise
terminate the process unhandled, before the process exits.

#### Scenario: Unhandled exception is logged before exit
- **WHEN** an exception propagates out of the host's top-level run call
- **THEN** the exception is logged (including its message and stack trace) and all buffered log
  output is flushed to the console and file sinks before the process terminates

### Requirement: Log level configuration
Each host's minimum log level and per-category overrides SHALL be configurable through its existing
`Logging` configuration section, without requiring a new configuration schema.

#### Scenario: Default level applies
- **WHEN** no override is configured for a given category
- **THEN** log entries for that category are emitted at or above the configured `Logging:LogLevel:Default` level and suppressed below it

#### Scenario: Category override applies
- **WHEN** a category has an explicit level configured under `Logging:LogLevel`
- **THEN** log entries for that category are emitted at or above the overridden level, regardless of the default level
