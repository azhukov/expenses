# Architecture

Clean Architecture — ports and adapters — in four rings across five production projects, two of
which are adapter hosts (D1). Dependencies point inward only.

This file is a **rendering, not an authority**: every decision it shows is decided in
[`openspec/changes/add-personal-expense-ledger/design.md`](../openspec/changes/add-personal-expense-ledger/design.md)
and cited here as `D1`–`D34`. Where the two disagree, the design document wins and this file is
wrong. [CLAUDE.md](CLAUDE.md) states the working rules the shape implies.

The ring rules are not a convention to be reviewed — they are asserted by
[ProjectReferenceRulesTests.cs](tests/Expenses.Application.Tests/Architecture/ProjectReferenceRulesTests.cs),
so a `ProjectReference` or `PackageReference` that breaks a ring fails the build.

## The rings

```
     ┌─────────────────────────┐        ┌─────────────────────────┐
     │   Browser client (FE)   │        │  MCP client (assistant) │
     └────────────┬────────────┘        └────────────┬────────────┘
                  │ HTTP/JSON                        │ stdio  (or HTTP)
                  ▼                                  ▼
   ══════════════════════════════   ADAPTERS   ══════════════════════════════
     ┌─────────────────────────┐        ┌─────────────────────────┐
     │     Expenses.Api        │        │     Expenses.Mcp        │
     │  Endpoints, Contracts   │        │  PurchaseTools          │
     │  ErrorHandling          │        │  ExtractionTools        │
     │                         │        │  ReferenceDataTools     │
     │  capture is HTTP-only   │        │  NO image bytes         │
     └────────────┬────────────┘        └────────────┬────────────┘
                  │        composition only          │
                  └────────────────┬─────────────────┘
                                   ▼
   ═══════════════════════════   APPLICATION   ═══════════════════════════
     ┌──────────────────────────────────────────────────────────────────┐
     │  Expenses.Application          ──→ Domain                        │
     │                                                                  │
     │  USE CASES            Purchases · Merchants · Receipts           │
     │                       ReferenceData (Categories, Units)          │
     │  POLICY               ExtractionCascade · ArithmeticValidation   │
     │  ERRORS               ApplicationError · DomainErrorTranslation  │
     │                                                                  │
     │  PORTS (Abstractions/)                                           │
     │   IPurchaseRepository   IMerchantRepository   ICategoryRepository│
     │   IUnitRepository       IUnitOfWork           IClock             │
     │   IReceiptImageStore    ITemporaryReceiptStore                   │
     │   IReceiptExtractor     IExtractionCandidateStore                │
     └───────────────────────────────┬──────────────────────────────────┘
                                     │  implemented by ▼ (inverted)
   ═════════════════════════════   INFRASTRUCTURE   ════════════════════════
     ┌──────────────────────────────────────────────────────────────────┐
     │  Expenses.Infrastructure       ──→ Application                   │
     │                                                                  │
     │  Persistence/    ExpensesDbContext, Repositories, Configurations │
     │                  DatabaseProvisioning, DatabaseStartup, Seeds    │
     │  Receipts/       ReceiptFileStore (permanent, content-addressed) │
     │                  TemporaryReceiptFileStore (GUID-keyed, staging) │
     │                  OrphanCaptureSweep (daily hosted job)           │
     │  Extraction/     InMemoryExtractionCandidateStore (transient)    │
     │                  FiscalCodeDecoder, PlaceholderReceiptExtractor  │
     │                  Steps: FiscalStep, VisionStep                   │
     │  ExpensesInfrastructure.cs   ← the single DI wiring point        │
     └───────────────────────────────┬──────────────────────────────────┘
                                     ▼  EF Core / Npgsql
                        ┌────────────────────────────┐
                        │  PostgreSQL 17  (ICU 'und')│
                        │  rows + receipt bytes      │
                        └────────────────────────────┘

   ══════════════════════════════   DOMAIN   ══════════════════════════════
     ┌──────────────────────────────────────────────────────────────────┐
     │  Expenses.Domain    ── zero dependencies, not even NuGet         │
     │  Purchase · Expense · Merchant · Category · Unit                 │
     │  Receipt (a value on Purchase) · Extraction/ExtractionCandidate  │
     │  Entities only (D22): no validators, no exceptions, no records   │
     └──────────────────────────────────────────────────────────────────┘
```

As project references:

```
Expenses.Domain          no dependencies at all — not even NuGet
Expenses.Application     -> Domain                use cases + ports
Expenses.Infrastructure  -> Application           EF Core, storage, extraction, DI wiring
Expenses.Api             -> Application, Infrastructure (composition only)
Expenses.Mcp             -> Application, Infrastructure (composition only)
```

## The capture-and-confirm path — extraction is synchronous

Capture and re-run both call the pipeline directly, in the request — **two ordered steps** (D28),
with no queue and no `Pending`/`Extracting` state observable between requests:

```
  POST /receipts/capture ──► ITemporaryReceiptStore   ──► ExtractionCascade ──► response
   (Api only, no purchase)    GUID-keyed staging file        (synchronous, in this request)
                                                                    │
        step 1  FiscalStep   free  │  decode the QR (or use the supplied payload),
                                   │  then ask the verification service for the invoice
        step 2  VisionStep   paid  │  placeholder today; runs only if step 1 read no lines
                                              │
                                              ▼
                            ArithmeticValidation  ◄── runs ONCE, here, and only labels
                                              ▼
                              Extracted │ NeedsReview │ Failed
                                              │
        caller confirms: date, amount, lines + the temp key
                                              ▼
        RecordPurchase ──► IReceiptImageStore.Save (promote) ──► Purchase.Record(..., receipt)
                                              │
                                    ITemporaryReceiptStore.Delete
```

**A step advances the run only by reading no lines.** Arithmetic does not route anything: it is a
pure function applied once, by `ReceiptService`, to whatever the run produced, and it decides only
`Extracted` versus `NeedsReview`. A result that does not reconcile is surfaced for review with its
report — it is never handed to another engine to be re-guessed, which is what keeps an invoice the
tax authority itself stated from being second-guessed over a rounding difference (D22, D28).

`Failed` means one thing only: no step read any lines at all.

Fiscal identity threads forward as an optional parameter. A step that establishes it without reading
lines passes it on, so the vision step receives a known-true total and issuer identity even where the
verification service could not be reached (D23, D29). The payload behind it is retained on the
receipt, so a re-run reads it rather than decoding the photograph again (D32).

Fiscal QR decode in the browser, at capture, is the intended primary path and belongs to `FE/`. The
backend contract is shaped for it: `POST /receipts/capture` accepts the raw payload as `fiscalQr`,
parsed server-side by the one parser that reads this format (D30).

Capture's candidates are never held server-side at all: they travel in the capture response, and
the caller resubmits what it needs — verbatim or edited — to confirm. `RerunExtraction`, for a
receipt already attached to a purchase, still holds candidates in memory keyed by purchase (D12): a
restart loses the suggestion and keeps every confirmed line and the extraction state recorded on the
purchase. Reading reports that absence rather than an empty extraction, and never re-runs the
cascade on its own.

The one background job left is `OrphanCaptureSweep`: once a day it deletes temporary captures nobody
confirmed within a day. It has no queue-shaped complexity — no per-item retry, no ordering — because
"old enough" is the only thing it ever decides. See [README.md](README.md) for the cascade's
configuration keys and what the placeholder extractor does.

## What the shape buys

1. **One Application layer, two front doors (D1).** No rule lives in an adapter; `Api` and `Mcp` are
   composition only. That is what lets
   [CrossAdapterTests](tests/Expenses.Integration.Tests/Ledger/CrossAdapterTests.cs) assert the same
   operation gets the same validation outcome over either interface — and only running both proves
   it.
2. **Dependency inversion at the Infrastructure boundary.** Application declares the ports;
   Infrastructure implements them and is registered in exactly one place,
   [ExpensesInfrastructure.cs](Expenses.Infrastructure/ExpensesInfrastructure.cs). Replacing the
   placeholder extractor with a real vision engine is one registration change and nothing above the
   port moves (D20).
3. **Testability follows the rings.** `Domain.Tests` and `Application.Tests` need nothing at all;
   only `Integration.Tests` needs Docker (D17).

## Not yet built

- [FE/](../FE/) is empty. The browser client at the top of the diagram, and stage 0 with it, does
  not exist yet.
- `Expenses.Mcp` still contains a template `Worker.cs` that logs the time every second — scaffolding
  from `dotnet new`, not part of this design.
