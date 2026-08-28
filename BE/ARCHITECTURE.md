# Architecture

Clean Architecture — ports and adapters — in four rings across five production projects, two of
which are adapter hosts (D1). Dependencies point inward only.

This file is a **rendering, not an authority**: every decision it shows is decided in
[`openspec/changes/add-personal-expense-ledger/design.md`](../openspec/changes/add-personal-expense-ledger/design.md)
and cited here as `D1`–`D22`. Where the two disagree, the design document wins and this file is
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
     │  images uploadable HERE │        │  NO image bytes         │
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
     │   IReceiptImageStore    IReceiptExtractor                        │
     │   IExtractionQueue      IExtractionCandidateStore                │
     └───────────────────────────────┬──────────────────────────────────┘
                                     │  implemented by ▼ (inverted)
   ═════════════════════════════   INFRASTRUCTURE   ════════════════════════
     ┌──────────────────────────────────────────────────────────────────┐
     │  Expenses.Infrastructure       ──→ Application                   │
     │                                                                  │
     │  Persistence/    ExpensesDbContext, Repositories, Configurations │
     │                  DatabaseProvisioning, DatabaseStartup, Seeds    │
     │  Receipts/       ReceiptFileStore (files, content-addressed)     │
     │  Extraction/     ExtractionQueue (bounded Channel)               │
     │                  InMemoryExtractionCandidateStore (transient)   │
     │                  FiscalCodeDecoder, PlaceholderReceiptExtractor  │
     │                  Stages: FiscalDecodeStage, VisionStage          │
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
     │  Receipt (a value on Purchase) · Extraction/ExtractionResult     │
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

## The extraction path — the one asynchronous flow

Extraction runs off the request path (D12) as an ordered cascade, cheapest stage first (D20):

```
  POST image ──► ReceiptFileStore  ──► IExtractionQueue ──► 202, request ends
   (Api only)     file + reference     bounded Channel(256)
                                              │
                                              ▼  background drain (in-process)
                                       ExtractionCascade
                                              │
        stage 1  FiscalDecodeStage    free    │  opportunistic — a miss is normal
        stage 2  VisionStage cheap    paid    │  placeholder today
        stage 3  ArithmeticValidation free    │  ◄── decides the outcome
        stage 4  VisionStage expensive paid   │  runs only if stage 3 failed
                                              ▼
                              Extracted │ NeedsReview │ Failed
```

Stage 0 — fiscal QR decode in the browser, at capture — is the intended primary path and belongs to
`FE/`. The backend contract is already shaped for it: fiscal identifiers are accepted alongside an
upload, not only discovered from one.

What the cascade produces is held in memory, keyed by purchase, and never written to a table (D12):
a candidate exists to be confirmed into an expense or discarded, so a restart loses the suggestion
and keeps every confirmed line and the extraction state recorded on the purchase. Reading reports
that absence rather than an empty extraction, and never re-runs the cascade on its own.

The queue is bounded on purpose, so restart loses queued-but-unrun work by design; a startup sweep
re-finds anything still `Pending` or stranded `Extracting`. See [README.md](README.md) for the cascade's configuration keys
and what the placeholder extractor does.

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
