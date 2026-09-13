## Context

See proposal.md — Why. What matters here is which of the earlier decisions this change amends, and
which measurements it rests on.

**Amends D20 and D22.** D20 established the cascade and made arithmetic validation the gate that
separates `Extracted` from `NeedsReview`. D22 made fiscal decoding primary and vision the fallback.
Both stand in substance; this change narrows them. The cascade becomes two steps, and the oracle
stops being control flow. **Where this contradicts D20's stage table or D22's stage ordering, this
one wins.**

**The measurements this rests on are already recorded** in
`BE/Expenses.Infrastructure/Extraction/FISCAL-QR.md` and are not re-derived here: zxing-cpp reads
one of three photographed thermal receipts and cropping buys nothing; the portal's contract is
undocumented and observed from one invoice; `crtd` carries a `+` that a form decoder destroys; the
portal is fragment-routed. The one thing that file records as unknown — **whether the portal answers
for an old invoice** — is still unknown and is called out under Risks.

**Current shape, for reference.** `ExtractionCascade` sorts stages by `ExtractionStageRole`, runs
every `Opportunistic` stage for its fiscal identity, then runs producing stages until one passes
`ArithmeticValidator`, keeping whichever failed fewer checks. `FiscalTrail` merges identity
field-by-field under an exactness ladder and hands it forward through
`ExtractionStageRequest.Known`.

## Goals / Non-Goals

**Goals:**

- One uniform step signature, with no channel between steps other than the result.
- Arithmetic validation as a pure labelling function called once, by the caller, not by the pipeline.
- A fiscal QR payload that a client can supply and the server can store, parsed by one parser.
- Fewer types: one result shape, no role enum, no trail.

**Non-Goals:**

- **Introducing a real vision engine.** The placeholder stays. This change shapes the step so that
  swapping the engine is a one-adapter change; the swap itself is the next change.
- **Browser-side QR decoding.** The endpoint accepts a payload; nothing in `FE/` produces one yet.
- **Changing the arithmetic checks.** The three checks and the two-decimal comparison (D25) are
  untouched. Only *when* they run changes.
- **Answering the portal-retention question.** Called out as a risk; a spike is not in this change.

## Decisions

### D28 — Two steps, and a step advances only when it produced nothing

The advance condition changes from "the result failed validation" to "the step produced no candidate
lines". Three things follow, and the third is the reason.

*A step is either useful or absent.* Under the old rule a step could run, produce a usable result,
and still be overruled by a later engine. Under the new one a produced result is the answer, and the
only question left is whether to trust it — which is a labelling question, not a routing one.

*Best-of-failures dies with it.* `Better()` compared two reports by failure count. With one
probabilistic step there is never a second result to compare, so the comparison is not simplified,
it is deleted.

*And the deterministic result stops being second-guessable.* This is what D22 wanted and could not
quite express: a retrieved invoice is the tax authority's own statement of the invoice. Under the
old rule, a retrieved invoice that failed a check — and the one invoice ever observed *nearly* did,
its lines summing to `59.6515` against a stated `59.65` — would hand the receipt to a probabilistic
engine to be re-guessed. D25's rounding fixed that specific case; the new rule removes the class.

**Alternative considered — keep validation as the gate, drop only the second tier.** Rejected: it
keeps the whole `report?.Passed` loop condition and the coupling between the validator and the
pipeline for the sake of one escalation path that now escalates to nothing.

### D29 — Fiscal identity is one optional parameter, threaded forward

`FiscalTrail` exists because two stages could establish the same field and something had to rank
them: hence the exactness ladder (`None < ReadAsText < Decoded`). With decode folded into the fiscal
step, only one step can ever decode and only one can ever read-as-text, and they run in that order —
so **step order supplies the ranking the ladder was computing**. The parameter is merged field by
field, first writer wins, and that is the whole rule.

It is threaded forward rather than only recorded because the vision step wants it. A payload that
decoded but whose portal call failed still states the invoice total and the issuer tax number, and a
vision step told "this receipt totals 59.65" is anchored on the hardest number on the page. This is
D23 restated structurally rather than as a special case.

**Alternative considered — pass the raw payload string between steps instead of parsed identifiers.**
Rejected: the vision step wants the total as a number and the portal wants three named parameters;
both would have to parse. Parsing once at the boundary and threading the parsed form is fewer moving
parts.

### D30 — The capture endpoint takes the raw payload, and the parser moves to the boundary

`fiscalIkof` and `fiscalJikr` are removed rather than kept alongside `fiscalQr`. FE and BE ship
together and there are no third-party clients, so two ways to state the same fact is cost without
benefit.

The raw payload wins over parsed fields for a specific reason: **the portal needs `iic`, `tin` and
`dateTimeCreated`, and the old contract could carry only the first.** A client-decoded QR could
never drive the lookup it was decoded for. Beyond that, the payload carries `prc` for free, and it
keeps `FiscalIdentity.From` as the single implementation of a format with two documented traps —
fragment routing, and the `+` in the UTC offset that any form decoder turns into a space.

`FiscalIdentity` therefore stops being `internal` to Infrastructure and becomes reachable from the
API layer. Parsing happens at the edge; the pipeline never sees a raw string.

Two boundary properties, both stated in the spec because both are silently breakable:

- **The payload is length-bounded** before it reaches the parser.
- **The payload is a URL that is never dereferenced.** `FiscalPortalClient` takes its endpoint from
  `PortalOptions`; only the payload's *parameters* are read. This is the difference between parsing
  a URL and fetching one, and it would be an easy edit to get wrong later.

`FiscalIdentity.From` is already total — an unrecognised string becomes a bare IKOF, no throw path —
which is the behaviour a trust boundary needs, so no change there.

### D31 — Decode only when no payload was supplied, and prefer the supplied one outright

The condition is **"is this sufficient to call the portal"**, not "is anything present". Under the
old contract a client could supply an IKOF and nothing else, which is present and useless. With
D30 in place a supplied payload is normally complete, but a partial parse is still possible, so the
guard is expressed against what the portal needs.

This **removes the supplied-versus-extracted disagreement check**, and that is a deliberate loss.
The old design retained both readings and reported a conflict, on the principle that preferring one
source silently could hide a misread receipt. The counter-argument is measurement: a client reading
a live camera has autofocus and retries; the server has one still frame and reads one symbol in
three. Cross-checking the better source against the worse one mostly produces a false alarm or
nothing. We prefer the client's reading and say so, rather than arriving there by omission.

### D32 — The payload is persisted, nullable, on the receipt

One nullable text column on `Purchases` (a receipt has no identity of its own — D11) and a
migration. Roughly 200 bytes.

The reason is re-runs, not archaeology. `RerunExtraction` currently re-reads the image and
re-decodes it, inheriting the same one-in-three odds every time — so a receipt whose QR the *client*
read successfully at capture loses that advantage on every subsequent run. A stored payload makes
the fiscal step's input permanent. The secondary benefit is that FISCAL-QR.md records the response
shape as observed from exactly one invoice, so learning a new field later is expected; a stored
payload lets that be recovered without the image.

It is stored **whether or not it parsed**, since the unparseable case is precisely the one a future
parser would want back.

This shrinks the capture→confirm round trip from six fiscal members
(`SuppliedIkof`, `SuppliedJikr`, `ExtractedIkof`, `ExtractedJikr`, `FiscalExtractedSource`,
`FiscalCreatedAt`) to three: the payload, the JIKR — knowable only from the portal's `fic` (D24) —
and the source.

### D34 — An extraction result is an application type, not a domain one

**Amends D12.** D12 put `ExtractionResult` in `Expenses.Domain.Extraction` on the reasoning that
candidates must be held apart from the aggregate so that an unconfirmed extraction can exist without
the purchase ever being invalid. That reasoning stands. What does not follow from it is that the
result belongs in the domain project: "held apart from the aggregate" is satisfied by not being part
of `Purchase`, not by sharing an assembly with it.

`ExtractionResult` is therefore deleted and its content folded into `ExtractionStepResult`, together
with `ExtractionStageOutcome` and `RetrievedInvoice`. One type is what a step returns, and it
carries the lines, the engine identity, the per-value provenance, the fiscal identity the step
established, and the payload it decoded.

**The split that survives is between the line and the envelope.** `ExtractionCandidate` stays in
`Expenses.Domain.Extraction`: a proposed line is domain vocabulary, because it is what becomes an
`Expense` on confirmation. What one step produced — engine name, stages run, fiscal source, payload
— is not; it is a fact about a run of the pipeline, and the pipeline is an application concern. The
old arrangement had the envelope in the domain purely because the lines were, which is inheritance
by adjacency rather than by argument.

**What this costs.** `Expenses.Domain` no longer names the thing extraction produces, so a reader
looking for it starts in `Expenses.Application.Dtos`. Against that: the result was never persisted,
never validated by the domain, and had no invariant of its own beyond requiring an engine name —
which is to say it was a record shaped like an entity.

**Alternative considered — collapse only the two application types and leave `ExtractionResult` in
the domain.** Rejected on instruction. It would have left a domain type as the payload of an
application record, which is the split this change exists to remove.

### D33 — Types, and what is deleted

| Deleted | Replaced by |
| --- | --- |
| `ExtractionStageRole` | registration order |
| `FiscalTrail` and its exactness ladder | step order; first writer wins per field (D29) |
| `ExtractionStageRequest.Known` | the fiscal parameter on the uniform request |
| `Better()` / best-of-failures | nothing; there is never a second result |
| `RetrievedInvoice` | the fiscal section of the one result type |
| `ExtractionStageOutcome`, `ExtractionResult` | one `ExtractionStepResult`, nullable to mean "produced nothing" (D34) |
| `ArithmeticValidator` called inside the loop | called once by `ReceiptService` |

The payload stays a `string?` and the identifiers stay primitives; no wrapper type is introduced for
either.

## Risks / Trade-offs

- **The portal may not answer for old invoices, and this is still untested** → If retention is short,
  the fiscal step serves only recent receipts and the vision step is the permanent main path rather
  than the fallback — which changes what is worth investing in, though not the shape built here.
  Mitigated by the shape being indifferent: two ordered steps behave correctly whichever one carries
  the traffic. A one-hour spike against a real old IKOF answers it and should precede the *next*
  change, not this one.

- **Removing the disagreement check trusts the client's decode** → A client that supplies a payload
  from the wrong receipt now goes undetected where it previously surfaced as NeedsReview. Mitigated
  by the payload being stored verbatim, so a wrong one is recoverable after the fact rather than
  merely suspected at the time; and by candidates always being confirmed by a human beside the
  stored image.

- **A non-reconciling authoritative invoice now reaches the user** → Previously it triggered another
  engine. It now reaches review with its arithmetic report. This is intended (D28) but it does mean
  the review screen carries a case it did not before, which the browser-client delta covers.

- **Three published spec scenarios become false** → "The expensive stage runs only on demand", "The
  expensive stage runs after a failed validation", and "Fallback also fails … the result retained is
  the one that failed fewer checks". This is a spec rewrite, not an extension; the tests derived
  from those scenarios are deleted rather than adjusted, and their deletion is a task in its own
  right so it cannot happen quietly.

- **The parser moves onto the trust boundary** → It is already total, but it now reads
  attacker-controlled input. Mitigated by the length bound (D30), by the never-dereference property
  being stated in the spec rather than only in a comment, and by the parser having no I/O.

## Migration Plan

1. Add the nullable payload column and its migration; nothing reads it yet.
2. Add `fiscalQr` to the capture endpoint and parse at the edge, with `fiscalIkof`/`fiscalJikr` still
   accepted, so BE and FE are never broken at the same time.
3. Reshape the pipeline to two steps and move validation to `ReceiptService`.
4. Switch `FE/` to send `fiscalQr`, and drop the disagreement review reason.
5. Remove `fiscalIkof`/`fiscalJikr` and the now-dead types.

Steps 2 and 5 are what make this deployable in a working tree without a broken intermediate state;
they are not backwards compatibility for outside clients, of which there are none.

**Rollback**: steps 3–5 are code-only and revert cleanly. Step 1's column is additive and nullable,
so a revert leaves it unread rather than requiring a down-migration.

## Open Questions

- **Where the vision step's fiscal context goes in the eventual prompt** — as a system instruction,
  as part of the user turn, or as a structured field. It cannot be settled against a placeholder
  that performs no image analysis, and it does not change the specs, the step's signature, or the
  task breakdown. It belongs to the change that introduces a real engine.
