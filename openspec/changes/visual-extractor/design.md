## Context

`VisionStep` (`Expenses.Infrastructure/Extraction/VisionStep.cs`) already sits at the end of the
extraction cascade (D28) and delegates to whatever `IReceiptExtractor` is registered. Today that
registration is `PlaceholderReceiptExtractor`, which performs no image analysis (D12). The port —
`EngineName`, `EngineVersion`, `Extract(ReceiptImageContent, FiscalIdentifiers, CancellationToken)`
returning `ExtractionStepResult?` — is unchanged by this design; only the adapter behind it and its
DI registration change. See proposal.md for the motivation.

`FiscalPortalClient` already establishes the pattern this design follows for an outbound call from
`Expenses.Infrastructure`: an `IHttpClientFactory`-created named client, an `Options` type bound from
configuration, every failure caught and turned into `null` rather than propagated (D26), and a test
suite that stands up a real local HTTP server (`FiscalPortalStub`) instead of mocking a handler.

## Goals / Non-Goals

**Goals:**
- Replace the placeholder behind `VisionStep` with an adapter that calls the Claude API's vision
  capability and maps its answer onto `ExtractionStepResult`.
- Keep every failure mode — unreachable, timeout, malformed answer, missing credential — an ordinary
  "produced nothing" outcome, matching how the rest of the cascade already behaves (D26, D20).
- Keep the placeholder available for tests that need a free, deterministic engine (cascade wiring,
  arithmetic-oracle tests already built against it).

**Non-Goals:**
- No change to `IReceiptExtractor`, `ExtractionStepResult`, `ExtractionCandidate`, or anything above
  the vision boundary — this is a same-shaped adapter swap (D12).
- No image preprocessing (cropping, rectification, compression) — D21 already removed that ladder
  for the deterministic path on the grounds that a measured miss needed none of it; nothing here
  reintroduces a preprocessing stage for vision without separate evidence it helps.
- No retry/backoff policy beyond the existing single-attempt-with-timeout pattern `FiscalPortalClient`
  uses — a personal-ledger-volume vision call that fails is cheap to re-run by hand (re-run
  extraction), so an internal retry ladder is not built.
- No prompt-engineering iteration loop as part of this change's scope beyond producing a first
  working prompt; refining extraction accuracy against real receipts is expected to be a fast-follow
  once real usage data exists.

## Decisions

### D29 — The engine is called over HTTP directly, not through an SDK package

The Claude API is called as a plain HTTP POST to the Messages API (`/v1/messages`) via
`IHttpClientFactory`, the same way `FiscalPortalClient` calls the fiscal portal, rather than pulling
in the Anthropic .NET SDK.

**Alternatives considered:** an SDK package would remove some hand-rolled request/response types, but
`Expenses.Infrastructure` already has zero third-party HTTP-client dependencies beyond what .NET
ships, and the request this adapter needs (one image, one prompt, one JSON response) is small enough
that a typed `HttpClient` call costs less than auditing and pinning a new package. If the request
shape grows materially (streaming, tool use, multi-turn), revisit.

### D30 — The receipt image is sent as base64-encoded inline content, not a file upload

The image bytes already held by `ReceiptImageContent` (D11's temporary/permanent store content, byte
array and content type) are base64-encoded directly into the Messages API request body as an image
content block, rather than uploaded to a separate Files API and referenced.

**Alternatives considered:** a Files API upload amortizes cost across repeated calls with the same
image, but extraction calls vision at most once per receipt per re-run (D28's cascade only reaches
vision when the deterministic path did not already produce a validated result), so there is nothing
to amortize; inline content is one request instead of two.

### D31 — The prompt asks for one JSON object matching the cascade's own shape

The prompt instructs the model to return a single JSON object whose fields mirror
`ExtractionCandidate`/`ExtractionStepResult` directly (line items with description, quantity, unit,
unit price, list price, discount, tax rate, amount; total; tax amount; tax rate; merchant name),
plus a per-field confidence for the values arithmetic cannot check (D20: description, merchant name,
category/unit guess). The adapter deserializes strictly and treats a shape the model didn't produce
as "produced nothing" (D26's rule extended to vision) — the same posture `FiscalPortalClient` already
takes toward `VerifiedInvoice`'s shape.

**Alternatives considered:** asking Claude to call a structured tool (tool-use / function-calling)
gives a schema-validated response instead of free-form JSON, and is the better long-term shape; it is
deferred rather than adopted now because it adds surface area (tool definitions, tool-result handling)
this first cut does not need to prove the approach — noted as an open question below rather than
built.

### D32 — Every failure — unreachable, timeout, unparseable, no credential — collapses to `null`

`Extract` catches `HttpRequestException`, `TaskCanceledException`/`OperationCanceledException`, and
`JsonException` exactly as `FiscalPortalClient.Fetch` does, and additionally treats a missing or
empty API key as an immediate `null` rather than an exception, checked before any request is sent.
None of these log above `LogDebug`, matching D26's stance that a stage producing nothing is an
ordinary outcome, not a problem with the receipt — and vision has no fallback behind it, so a failure
here is exactly a receipt with fewer candidates, never a thrown error surfaced to a caller.

**Alternatives considered:** raising a startup-time failure when no API key is configured was
considered, but the placeholder-based test/dev deployments (and any environment intentionally running
without paid vision) would then be unable to start at all; a per-call `null` keeps every existing
deployment mode working unchanged.

### D33 — The placeholder stays, selected by a test-only configuration switch

`PlaceholderReceiptExtractor` is not deleted. Production DI (`AddExtraction` in
`ExpensesInfrastructure.cs`) resolves `IReceiptExtractor` to the new Claude-backed adapter by
default, and `VisionStep` is constructed with whatever `IReceiptExtractor` DI hands it — same
pattern as `IFiscalInvoiceRetrieval`/`FiscalPortalClient`.

**Correction during implementation:** this decision originally said tests needing a free,
deterministic engine "continue to construct `PlaceholderReceiptExtractor` directly, as
`ExtractionPipelineTests` and others already do today." That was not true of the codebase at
implementation time: `PlaceholderReceiptExtractor` is `internal`, so no test can construct it
directly without `InternalsVisibleTo`, and every existing test reaches the vision stage through the
full `AddExpensesInfrastructure` composition root, not by constructing an engine by hand. Several
tests (`ExtractionPipelineTests`, the "a miss is reported no differently" scenarios in
`FiscalJourneyTests` and `DecoderRegressionTests`, and the `engineName == "placeholder"` assertions
in `McpAdapterTests`, `FiscalSourceReportingTests` and `HttpAdapterTests`) depend on the production
registration being the placeholder.

Rather than exposing `PlaceholderReceiptExtractor` outside the assembly, `AddExtraction` reads an
additional, undocumented (not in `appsettings.json`) configuration switch,
`Extraction:Vision:Engine`, exactly like the existing `Extraction:Placeholder:Outcome` test lever:
absent or any value other than `"placeholder"` resolves the real Claude adapter; `"placeholder"`
resolves `PlaceholderReceiptExtractor` instead. The tests above pass
`("Extraction:Vision:Engine", "placeholder")` alongside their other configuration overrides, the same
way they already pass `Extraction:Portal:BaseAddress` to point at a stub.

**Alternatives considered:** `InternalsVisibleTo` would let tests construct
`PlaceholderReceiptExtractor` literally, matching the original wording, but it exposes every other
internal type in `Expenses.Infrastructure` to the test project as a side effect, where a single
configuration switch mirroring `Extraction:Placeholder:Outcome` costs nothing extra and keeps the
assembly boundary intact.

### D34 — Test double is a real local HTTP server, following `FiscalPortalStub`

The new adapter's integration tests stand up a real Kestrel `WebApplication` bound to
`http://127.0.0.1:0`, mirroring `FiscalPortalStub`, rather than mocking `HttpMessageHandler`. The
stub answers `/v1/messages` with a fixture response (a recorded or hand-built Messages API response
shape) and can be configured to answer non-200, hang, or return a body missing the expected JSON
shape, exercising the same failure branches D32 introduces. This keeps the suite from ever reaching
the real Claude API, matching D26's rule for the fiscal portal.

## Risks / Trade-offs

- **[Risk] Vision calls cost real money per receipt, unlike the placeholder.** → Mitigation: D28
  already bounds vision to the fallback case (only receipts the deterministic path could not serve),
  and at personal-ledger volume this was already judged acceptable for the tier it replaces.
- **[Risk] A model response that is valid JSON but semantically wrong (hallucinated line items,
  wrong total) is indistinguishable from a correct one at the adapter layer.** → Mitigation: this is
  exactly what the arithmetic validator (already built, D28's cascade) exists to catch — a
  hallucinated total that doesn't sum against hallucinated lines fails the same checks a bad
  placeholder result would, landing the receipt in NeedsReview rather than silently corrupting the
  ledger.
- **[Risk] Prompt/response-shape drift across model versions.** → Mitigation: `EngineVersion` is
  recorded on every result (already required by the port), so a version bump that changes behavior is
  visible in stored provenance even though nothing enforces compatibility at call time.
- **[Trade-off] No retry means a single transient network blip fails the vision tier for that
  request.** → Accepted: re-running extraction is already a first-class, user-facing action (receipt-
  ingestion's "Extraction can be re-run" requirement), so a transient failure costs a manual re-run,
  not a lost receipt.

## Open Questions

- Whether to move to Claude's tool-use (structured output) API instead of free-form JSON-in-a-prompt
  once the first cut is validated against real receipts — deferred per D31, does not change the
  specs or the task breakdown, only the adapter's internals.
- Exact model id/version to default to, and whether it should be configurable per-deployment or
  pinned — left to the options type's default value at implementation time; changing it later is a
  configuration change, not a design change.
