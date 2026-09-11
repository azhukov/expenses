## 1. Configuration scaffolding

- [x] 1.1 Add `VisionOptions` (or similarly named options type) to `Expenses.Infrastructure/Extraction/`: model id, API key, base address, timeout milliseconds — following `PortalOptions`'s shape and defaults pattern. Scaffolding; no behavior of its own to test.
- [x] 1.2 Add the `Extraction:Vision` configuration section to `Expenses.Api/appsettings.json` and `Expenses.Mcp/appsettings.json` with safe non-secret defaults; document that the API key is supplied via environment/user-secrets, never committed. Scaffolding.

## 2. Test harness for the Claude API

- [x] 2.1 Add `ClaudeVisionStub` under `Expenses.Integration.Tests/Harness/`, a real local `WebApplication` bound to `http://127.0.0.1:0` (mirroring `FiscalPortalStub`) that serves `/v1/messages`, with modes: `Answering()` (a fixture response containing valid extraction JSON), `WithUnexpectedShape()` (200 with a body missing the expected fields), `Failing(status)` (non-200), `Hanging()` (accepts then never responds). Scaffolding for the tests in section 3; no behavior of the production system to test on its own.
- [x] 2.2 Add a fixture file with a realistic Messages API response wrapping an extraction JSON payload (line items, quantities, units, tax rate, total, merchant name, per-field confidence), used by `ClaudeVisionStub.Answering()`.

## 3. The vision adapter (test-first)

- [x] 3.0 Write the failing integration tests for the new adapter in `Expenses.Integration.Tests/Extraction/ClaudeVisionExtractorTests.cs`, covering (against `ClaudeVisionStub`, never the real API):
  - "The engine reads the image content" — two different fixture images extracted produce different candidate lines matching each fixture's expected content.
  - "Results are identified by engine" — result carries the adapter's `EngineName`/`EngineVersion`.
  - "An unverifiable value carries the engine's own confidence" — description/merchant-name confidence round-trips from the fixture response; an arithmetic-checkable value (amount) carries none.
  - "The engine cannot be reached" (`Hanging()`, short configured timeout) → `null`.
  - "The engine's answer cannot be interpreted" (`WithUnexpectedShape()`) → `null`.
  - "No credential is configured" — adapter constructed with an empty API key → `null`, without sending a request (assert the stub received no request).
  - A non-200 response (`Failing(HttpStatusCode.InternalServerError)` / `Unauthorized`) → `null`.
  - The outbound request shape: asserts the request the stub received carries the base64-encoded image and the expected model id, so a wire-format regression fails loudly.
- [x] 3.1 Implement `ClaudeVisionReceiptExtractor : IReceiptExtractor` in `Expenses.Infrastructure/Extraction/`: builds the Messages API request (D29, D30), sends it via a named `HttpClientFactory` client, and short-circuits to `null` before sending when no API key is configured (D32) — enough to turn on the credential-check test in 3.0.
- [x] 3.2 Implement response parsing and mapping onto `ExtractionStepResult`/`ExtractionCandidate` per the prompt's JSON shape (D31), including per-field confidence for unverifiable values (D20) — enough to turn on the mapping and confidence tests in 3.0.
- [x] 3.3 Implement the failure handling: catch `HttpRequestException`, `TaskCanceledException`/`OperationCanceledException`, `JsonException`, and non-success status codes, each returning `null` and logging at `LogDebug` only (D26, D32) — enough to turn on the remaining failure tests in 3.0.
- [x] 3.4 Run `dotnet test BE/Expenses.sln` and confirm every test written in 3.0 passes.

## 4. Wiring

- [x] 4.1 In `ExpensesInfrastructure.AddExtraction`, bind `VisionOptions` from `Extraction:Vision` and register `ClaudeVisionReceiptExtractor` (via a named `HttpClientFactory` client, following the `FiscalPortalClient` registration) as the `IReceiptExtractor` `VisionStep` is constructed with, replacing the `PlaceholderReceiptExtractor` registration. Leave `PlaceholderReceiptExtractor` itself in place, unregistered from production DI, for tests that construct it directly.
- [x] 4.1a **(added during implementation, see design.md D33's correction)** Add the `Extraction:Vision:Engine` test-only configuration switch (`"placeholder"` selects `PlaceholderReceiptExtractor`; anything else selects the real adapter) to `AddExtraction`, since `PlaceholderReceiptExtractor` is `internal` and no existing test constructs it directly despite what D33 originally assumed. Migrate every test whose assertions depend on the production vision registration being the placeholder to pass `("Extraction:Vision:Engine", "placeholder")`: `ExtractionPipelineTests` (all cases), `FiscalJourneyTests.A_miss_is_reported_no_differently_from_a_receipt_carrying_no_code`, `DecoderRegressionTests.A_miss_does_not_degrade_the_receipt`, `McpAdapterTests` (MCP session setup), `FiscalSourceReportingTests` (MCP session setup), `HttpAdapterTests` (API session setup), `ReceiptJourneyTests` (all cases). Run `dotnet test BE/Expenses.sln` and confirm the full suite passes with the production registration now real. Done: full suite (369 tests) passes.
- [x] 4.2 Write/adjust an integration test asserting the DI container resolves `ClaudeVisionReceiptExtractor` (not the placeholder) for `IExtractionStep`'s vision entry in the default configuration — a wiring regression test, not a new behavior, so it may follow its implementation task.

## 5. Spec and documentation upkeep

- [x] 5.1 Update `BE/README.md`'s extraction-configuration table to list `Extraction:Vision:*` settings alongside `Extraction:ConfidenceThreshold`, and update the paragraph currently stating the vision stage performs no image analysis. Documentation; no test.
- [x] 5.2 Confirm `openspec archive` will fold this change's spec delta cleanly into `openspec/specs/receipt-ingestion/spec.md` (run `openspec validate --strict` against the change before implementation work is considered complete). Scaffolding/process step; no test. Done: `openspec validate visual-extractor --strict` reports valid.
