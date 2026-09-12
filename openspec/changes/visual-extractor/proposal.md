## Why

The vision stage of the extraction cascade — the fallback for any receipt the deterministic path
(fiscal QR + portal retrieval) cannot serve — is still `PlaceholderReceiptExtractor`: it performs no
image analysis at all (D12). Every receipt that falls through to vision today produces fabricated
candidate lines rather than anything read from the photograph, so a receipt with no readable fiscal
code, or a merchant not on the national fiscalisation scheme, cannot actually be captured through
extraction. Replacing the placeholder with a real vision engine is the last piece the design already
budgeted for (D28) and was deferred behind — "one registration change" once a real engine exists.

## What Changes

- Add a real `IReceiptExtractor` implementation backed by the Claude API's vision capability: the
  receipt image is sent to a Claude model with a prompt instructing it to return the same shape the
  cascade already expects — line items, quantities, units, tax rate, total, merchant name — as
  structured JSON.
- Add configuration for the engine: model id, API key (read from configuration/environment, never
  committed), and a request timeout, following the same options-binding pattern as
  `PortalOptions`/`PlaceholderOptions`.
- Map the model's response onto `ExtractionCandidate`/`ExtractionStepResult`, attaching a
  model-reported confidence to the values arithmetic cannot check (description, merchant name,
  category/unit guesses) exactly as the placeholder does today, so `Extraction:ConfidenceThreshold`
  keeps working unchanged.
- Treat every failure mode (API error, timeout, malformed/unparseable response, missing API key)
  as the stage producing nothing — consistent with D26's rule for the fiscal portal — since vision is
  the last stage in the cascade (D28) and has no further fallback behind it.
- Wire the real extractor into `AddExtraction` in `ExpensesInfrastructure.cs`, replacing the
  `PlaceholderReceiptExtractor` registration for `VisionStep`. The placeholder implementation itself
  is kept for tests that need a deterministic engine (arithmetic-oracle tests, cascade tests already
  built against it).
- Update the "Vision extraction is pluggable and mocked in this change" requirement in
  `receipt-ingestion`, since vision is no longer only mocked once this ships.
- **BREAKING**: none to the public API surface — `IReceiptExtractor` is unchanged and this is a
  DI-registration swap behind an existing port. Operationally, running vision extraction now costs
  real money per call and requires a configured API key; without one, vision-tier receipts fail
  extraction (Failed state) rather than producing placeholder candidates.

## Capabilities

### New Capabilities

(none — this replaces an adapter behind an existing port; no new capability boundary is introduced)

### Modified Capabilities

- `receipt-ingestion`: the "Vision extraction is pluggable and mocked in this change" requirement
  changes — vision extraction is no longer a deterministic placeholder in production; it calls a
  real, probabilistic engine, and its failure modes (unreachable API, malformed response, missing
  credentials) must behave like every other optional stage: producing nothing rather than an error.

## Impact

- **Affected code**: `Expenses.Infrastructure/Extraction/` (new `ClaudeVisionReceiptExtractor` or
  similarly named adapter, new options type), `ExpensesInfrastructure.cs` (DI wiring), appsettings
  (`Extraction:Vision` configuration section, API key via environment/user-secrets).
- **Dependencies**: adds a dependency on the Anthropic API (HTTP call via `IHttpClientFactory`,
  consistent with how `FiscalPortalClient` already calls out) — either the official SDK or a direct
  HTTP client, decided in design.md.
- **Cost/operational**: vision calls are no longer free; they are billed per image and only run when
  the deterministic path did not already produce a validated result (D28 already bounds this to the
  fallback case).
- **Tests**: `PlaceholderReceiptExtractor` and its tests are retained for cascade/validation tests
  that need a deterministic, free engine; new integration tests exercise the real adapter's mapping
  and failure handling against a local HTTP stub, the same pattern `FiscalPortalStub` already
  establishes for the fiscal portal, so the suite never calls the real Claude API.
