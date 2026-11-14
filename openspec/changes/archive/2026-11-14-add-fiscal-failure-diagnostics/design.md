# Design

## Context

See proposal.md for the motivation. The relevant current shape:

- `FiscalStep` is the first step in `ExtractionCascade`. It returns `null` when there is no payload
  and `ExtractionStepResult.FiscalOnly(...)` when it got identifiers but no invoice. It catches any
  exception from retrieval and returns `null`.
- `IFiscalInvoiceRetrieval.Retrieve` (implemented by `FiscalPortalClient`) returns
  `ExtractionStepResult?`.
  - Each of these returns `null`: incomplete identifiers, a non-success status, an empty body, a
    `JsonException`, a network error, a timeout, and an invoice with no items.
  - Only successes are cached, keyed by IIC.
- `CascadeOutcome` → `CaptureResult` carries `FailureReason`. It is set only when no step read lines,
  and the text is generic ("No extraction step produced a result for this image.").
- `Capture.tsx` checks `state === 'Failed'` and shows one fixed notice. The not-fiscal vs. fiscal
  distinction already exists client-side (`supplied.ikof`/`issuerTaxNumber`), but only to decide
  what travels with the photograph.

Decision numbers continue the codebase's global sequence (the highest cited in code is D41).

## Goals / Non-Goals

**Goals:**

- Classify the reason at the point where it is known: the parser for the payload, the HTTP client
  for the portal. Carry it to the response without the cascade having to understand it.
- Keep D26's behavioural guarantee intact. A failure still means "this step produced nothing, carry
  on". Only the *reporting* changes.

**Non-Goals:**

- No diagnosis for the vision step. Its failures already surface as `Failed` with
  `FailureReason`.
- No persistence on `Purchase`, no re-run reporting, and no MCP field (capture is HTTP-only;
  re-run over MCP keeps its current shape).

## Decisions

### D42: A closed `FiscalDiagnosisKind` enum plus a small record

```csharp
public enum FiscalDiagnosisKind
{
    NoPayload, NotFiscal, IncompleteIdentifiers,
    PortalUnreachable, PortalRejected, NoRecord, UnparseableResponse,
    Retrieved,
}

public sealed record FiscalDiagnosis(
    FiscalDiagnosisKind Kind,
    string Detail,                                // short, English, for logs and as a UI fallback
    IReadOnlyList<string>? MissingParameters = null,  // "iic" | "tin" | "crtd"
    int? PortalStatus = null,
    string? ResponseExcerpt = null);              // ≤ 200 chars
```

It lives in `Expenses.Application/Dtos/Extraction`. It is serialised as a string enum, like
`ExtractionState` and `FiscalSource` already are.

- **Alternative: a free-text reason string.** Rejected. The client would have to pattern-match
  English to choose a message, and the test assertions would be brittle.
- **Alternative: an exception hierarchy.** Rejected. It contradicts D26's "nothing, not an error",
  and the cascade would need to catch and translate it.

`Retrieved` is included so that the field always tells the whole story for a capture that reached
the portal. A client can then distinguish "fetched" from "never got that far" without inferring it
from `fiscalSource`.

### D43: Retrieval returns a pair, not a nullable

`IFiscalInvoiceRetrieval.Retrieve` returns `FiscalRetrieval(ExtractionStepResult? Invoice,
FiscalDiagnosis Diagnosis)`. `FiscalPortalClient` sets the kind in each branch it already has:

| Branch | Kind |
|---|---|
| Missing `iic`/`tin`/`crtd` | `IncompleteIdentifiers` + `MissingParameters` |
| Cache hit | `Retrieved` |
| `HttpRequestException` | `PortalUnreachable` (detail: exception message) |
| `TaskCanceledException` when the caller's token is *not* cancelled | `PortalUnreachable` (detail: "timed out after N ms") |
| Non-success status | `PortalRejected` + `PortalStatus` |
| Blank body | `NoRecord` |
| `JsonException`, `null` deserialisation, or `Items` empty | `UnparseableResponse` + excerpt |
| Mapped | `Retrieved` |

The single `catch` that today groups network errors and JSON errors together is split, so that the
two cases are told apart. Cancellation by the caller still propagates.

Caching is unchanged: only successes are cached. A failure is retried on the next capture, which is
what makes "try again later" useful advice.

### D44: `FiscalStep` always answers and diagnoses the payload itself

`FiscalStep` no longer returns `null`. Each path returns `ExtractionStepResult.FiscalOnly(...)` (or
the invoice) with a new `Diagnosis` property set:

- No payload → `NoPayload`. The identifiers are empty, the source is `None` and the payload is
  `null`. The cascade's merge already treats this as contributing nothing. This also covers a
  decoder that throws.
- `FiscalIdentity.From` yields nothing → `NotFiscal`.
- Identifiers but no invoice → the retrieval's diagnosis. `IncompleteIdentifiers` is computed by the
  portal client (D43), because only it knows which three parameters it requires.
- `retrieval.Retrieve` throws unexpectedly (for example, `ExtractionCandidate.Propose` rejects a
  portal value while mapping) → `UnparseableResponse` with the exception's message as detail.

Returning a lineless result instead of `null` makes the cascade continue in the same way (the
`ProducedLines` rule, D28), so no run changes shape. `StepsRun` already records the step either way.

### D45: The cascade carries the first diagnosis it sees, opaquely

`ExtractionStepResult` gains `FiscalDiagnosis? Diagnosis`. `ExtractionCascade.Run` keeps the first
non-null diagnosis from any step and puts it on `CascadeOutcome.Diagnosis`, whether the run ends in
lines or not. `ReceiptService.Capture`/`CaptureFiscal` copy it onto
`CaptureResult.FiscalDiagnosis`, which is a new trailing optional parameter. The cascade doesn't
interpret the diagnosis. That keeps fiscal knowledge in the fiscal step (D29).

A photograph capture whose vision step succeeds still reports why the fiscal path didn't. This is
cheap, and it explains why a receipt shows a vision-engine result instead of the portal's.

### D46: Warning-level logging at the classification point

Every non-`Retrieved` diagnosis from the portal client is logged at Warning, with kind, status and
IIC. `NotFiscal`/`NoPayload` stay at Debug: they are about the paper, not the system. The existing
Debug logs with exceptions are kept as Warning with the exception attached. The response excerpt is
not logged beyond the same 200 characters.

### D47: Client message mapping

A pure function `fiscalNoticeOf(diagnosis: FiscalDiagnosis | null): { message: string; showPayload:
boolean }` sits beside `capture/review.ts`. `Capture.tsx` renders its message in place of the fixed
string. When `showPayload` is set, it also renders the read payload in a `<code>` block that wraps
long URLs (`overflow-wrap: anywhere`). The messages are:

- `PortalUnreachable`: "The fiscal service could not be reached. Photograph the receipt, or try
  again later."
- `PortalRejected`: "The fiscal service refused the request (HTTP {status})."
- `NoRecord`: "The fiscal service has no record of this invoice yet."
- `UnparseableResponse`: "The fiscal service answered, but its response could not be read."
- `NotFiscal`: "This code is not a fiscal receipt code. Read: …"
- `IncompleteIdentifiers`: "The code is missing {names}. Read: …"
- `NoPayload`: not expected on capture-fiscal. The generic message is used.
- `null`: the generic message.

The parameter names are shown in readable form: invoice code, issuer tax number, and creation time.
The rule for whether the payload travels with the photograph (`wasFiscal`) is unchanged.

## Risks / Trade-offs

- **The portal changes its error semantics.** For example, it might answer 200 with an error JSON.
  That case lands in `UnparseableResponse`, and the excerpt is there to make it diagnosable. This is
  better than today, where it is invisible.
- **The excerpt could carry personal data from an invoice body.** It is truncated to 200 characters,
  only ever sent back to the person who scanned that receipt, and it is never persisted.
- **Changing `IFiscalInvoiceRetrieval`'s signature touches tests.** `FiscalPortalTests` assert
  `null`. They are updated to assert the kind, which is strictly more precise.
- **Showing a raw URL on a phone screen.** It is wrapped and only shown for payload-related kinds.

## Migration Plan

This is additive. There is no schema change, and the new JSON field is ignored by older clients.
To roll back, revert the change. Nothing stored depends on it.
