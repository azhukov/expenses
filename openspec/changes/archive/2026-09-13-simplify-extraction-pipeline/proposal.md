## Why

The cascade built by D20 and amended by D22 carries three mechanisms that exist only to let stages
exchange data and second-guess one another: a `FiscalTrail` with an exactness ladder, a `Known`
channel threaded through every stage request, and a best-of-failures comparison between competing
results. Two of the three producing stages are placeholders over the same engine, so the
comparison has never compared anything real, and at personal-ledger volume the cheap-tier /
expensive-tier ladder it exists to serve saves single-digit dollars a year.

At the same time the deterministic path cannot actually be driven by the client that is best placed
to feed it. The capture endpoint accepts `fiscalIkof` and `fiscalJikr`; the verification portal
requires `iic`, `tin` and `dateTimeCreated`. Two of the three parameters have no way in, so a QR a
browser decoded at capture — with autofocus and retries, against a server-side decoder measured at
one hit in three — is presently dead weight for the lookup it was decoded for.

## What Changes

- **BREAKING** — extraction becomes **two ordered steps**, `fiscal` and `vision`. Fiscal-code
  decoding stops being a stage of its own and becomes the first step's own input-gathering.
- **BREAKING** — arithmetic validation stops being control flow. It runs **once, after the
  pipeline**, and its only job is choosing between `Extracted` and `NeedsReview`. A step advances
  to the next step only when it produced **no lines at all**; a result that does not reconcile is
  surfaced for review rather than handed to another engine.
- **BREAKING** — the expensive vision tier is removed. One vision step, one model. With a single
  probabilistic result there is nothing left to compare, so best-of-failures goes with it.
- **BREAKING** — the capture endpoint accepts `fiscalQr`, the **raw QR payload as a string**, and no
  longer accepts `fiscalIkof` / `fiscalJikr`. The payload is parsed server-side by the one parser
  that already knows this format's two traps: fragment routing, and the `+` in the UTC offset.
- Fiscal identity becomes a single **optional parameter** threaded through the pipeline: supplied by
  the client, or decoded from the image when absent, and passed forward to the next step when a
  step fails. The vision step receives it as context — a known-true total and issuer tax number,
  which is worth having even when the portal never answers (D23).
- The first step decodes the image **only when no payload was supplied**. A supplied payload is
  preferred outright rather than cross-checked, so the supplied-versus-extracted disagreement
  reporting is removed.
- The raw payload is **persisted** on the receipt, nullable. A re-run then feeds the fiscal step
  from stored text rather than re-decoding a photograph at the same one-in-three odds it had the
  first time.
- The capture-to-confirm round trip shrinks from six fiscal members to three: the payload, the JIKR
  (knowable only from the portal), and the source.
- Removed with the above: `ExtractionStageRole`, `FiscalTrail`, `ExtractionStageRequest.Known`,
  `RetrievedInvoice`, and the `ExtractionStageOutcome` / `ExtractionResult` split.

## Capabilities

### New Capabilities

None. This change reshapes existing behaviour rather than introducing a capability.

### Modified Capabilities

- `receipt-ingestion`: the cascade requirement is rewritten rather than extended. Three of its
  scenarios become false — "The expensive stage runs only on demand", "The expensive stage runs
  after a failed validation", and "Fallback also fails … the result retained is the one that failed
  fewer checks". Arithmetic validation is restated as a labelling step rather than a gate, the
  vision boundary requirement loses its two-tier language, and the stored fiscal payload is added.
- `api-surface`: "Fiscal identifiers may accompany a capture" becomes a raw payload accompanying a
  capture, and the corroboration reporting that depended on comparing two sources is removed.
- `browser-client`: the "Disagreeing fiscal identifiers are shown" review reason no longer exists,
  since one source is now preferred outright rather than cross-checked. The client does not decode
  QRs today and this change does not make it start — it forwards a payload field when one is
  present, and browser-side decoding at capture stays a separate change.

## Impact

- **Application** — `ExtractionCascade` and every type under `Dtos/Extraction`; `IExtractionStage`,
  `IFiscalCodeDecoder`, `IFiscalInvoiceRetrieval`, `IReceiptExtractor`; `ReceiptService`
  (validation moves here, and the state decision with it).
- **Infrastructure** — `Stages.cs`, `FiscalCodeDecoder`, `FiscalIdentity` (promoted out of
  `internal`, since the API layer now parses payloads at the edge), `FiscalPortalClient`,
  `PlaceholderReceiptExtractor`, and the extraction registrations in `ExpensesInfrastructure`.
- **Domain** — `Receipt` gains a nullable fiscal payload; `Receipt.FiscalSource` keeps its members
  but loses the disagreement path that consumed them.
- **Persistence** — one nullable text column on `Purchases`, and a migration.
- **API** — `POST /captures` form fields; `CapturedReceiptRequest`.
- **FE** — `writes.ts` capture call, the capture/review screens' fiscal handling, `types.ts`.
- **Specs** — deletions, not only additions, in `receipt-ingestion`.
- **Open** — this change does **not** introduce a real vision engine. The placeholder stays behind
  the single vision step; replacing it is the next change, and the step's shape is what makes that
  a drop-in.
