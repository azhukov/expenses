# Expenses backend

A personal expense ledger with two front doors — HTTP for the browser client, MCP for an assistant —
over one Application layer. See
[`openspec/changes/add-personal-expense-ledger/design.md`](../openspec/changes/add-personal-expense-ledger/design.md)
for the decisions this README refers to as `D1`–`D23`, [ARCHITECTURE.md](ARCHITECTURE.md) for the
shape of the rings and the extraction flow, and [CLAUDE.md](CLAUDE.md) for the ring rules.

## Running locally

The whole stack — database, HTTP host, MCP host over HTTP. Compose lives at the repository root,
because `FE/` will be a service in it too; run these from there:

```bash
docker compose up -d --build
```

| | | |
| --- | --- | --- |
| API | <http://localhost:5082> | Swagger UI at `/swagger`, OpenAPI at `/openapi/v1.json` |
| MCP | <http://localhost:5083> | HTTP transport; stdio is a `dotnet run` on the host machine |
| PostgreSQL | `localhost:5432` | user, password and database all `expenses` |

Only the API container applies migrations, because only it runs as Development (D15); the MCP
container waits on the API's healthcheck rather than merely on the database, so it never starts
against a database whose schema is not there yet.

Just the database — which is all the test suite and a locally-run host need:

```bash
docker compose up -d postgres   # provisioned by db/init
dotnet build BE/Expenses.sln
dotnet test  BE/Expenses.sln     # integration tests need Docker
```

Both hosts read their connection string from `ConnectionStrings:Expenses`; nothing in
`appsettings.json` supplies it, so a locally-run host needs `ConnectionStrings__Expenses` in its
environment or in user secrets. Compose sets it for the containers.

## Database provisioning

**Encoding, locale provider and collation are decided when the database is created and cannot be
changed afterwards** — not by EF, not by `ALTER DATABASE`. Changing them means creating a new
database and restoring into it. They are therefore *not* managed by EF Migrations (D13):

```sql
CREATE DATABASE expenses
  ENCODING 'UTF8' LOCALE_PROVIDER icu ICU_LOCALE 'und' TEMPLATE template0;
```

That statement lives in [db/init/01-create-database.sql](db/init/01-create-database.sql), which is
mounted into `docker-entrypoint-initdb.d` locally and must be part of whatever provisions any other
environment. The integration suite runs the same file against its throwaway container, so tests meet
the database production does.

Getting this wrong breaks nothing visibly — sorting is simply quietly incorrect — so
`DatabaseProvisioning.Verify` runs at startup in **every** environment and fails loudly, naming both
the fault and the fact that the fix is to recreate the database.

Init scripts run only on an empty data directory. After changing provisioning locally:
`docker compose down -v && docker compose up -d`.

## The receipt store

Receipt bytes are **files**, not rows: `Purchases` holds the hash, type, size and
`ReceiptStorageKey` of each image, and the bytes live under a configured root (D11). The root
comes from `Receipts:RootPath` — `Receipts__RootPath` in the environment — and defaults to
`receipts/` beside the host binary. Compose points both hosts at one `expenses-receipts` volume.

Files are content-addressed: `<root>/<aa>/<bb>/<sha256><ext>`, so identical bytes are one file that
two purchases may share, and the extension comes from sniffing the content rather than from the
uploaded name. The file is written before the reference is recorded and deleted after it is cleared,
so an interruption can leave an unreferenced file but never a purchase pointing at a missing one.
`ReceiptFileStore.Verify` runs at startup beside the provisioning check: a missing or unwritable
store stops the host rather than surfacing at the first upload.

**A database dump is no longer a complete backup.** Back the receipt root up beside it, from the
same point in time; neither is reproducible from the other.

## Migrations

Migrations live in `Expenses.Infrastructure/Persistence/Migrations`. An
`IDesignTimeDbContextFactory` means `dotnet ef` needs no `--startup-project` and neither host is
privileged over the other (D15). Point it at a database with `EXPENSES_CONNECTION` if the default
local one is not what you want.

```bash
cd BE/Expenses.Infrastructure
dotnet ef migrations add <Name> --output-dir Persistence/Migrations
dotnet ef migrations bundle  --output ../artifacts/migrate     # deployment step elsewhere
```

Development applies migrations on startup for convenience. Anywhere else runs the bundle as a
deployment step: two hosts migrating on startup would race, and neither should hold DDL rights at
runtime.

Seeding is split by mutability (D15): `Units` are `HasData` rows because nobody edits them,
categories are upserted by `Code` so a user's rename and a user's own categories survive a re-run.

## Extraction: the cascade

Extraction is not one engine but an ordered sequence of stages, cheapest first (D20):

```
  stage 0  fiscal QR decode (client, at capture)   free      out of scope here — belongs in FE/
  stage 1  fiscal QR decode (server, on the file)  free      real — zxing-cpp
  stage 2  fiscal invoice retrieval                free      real — the national verification portal
  stage 3  arithmetic validation                   free      real; runs after every producing stage
  stage 4  vision extraction                       paid      real — the Claude API's vision capability
```

Stage 2 is the deterministic extractor: where the QR decodes, the whole invoice is retrieved from
the service that issued it and used verbatim. A result that reconciles ends the cascade, so stage 4
runs only for a receipt the deterministic path could not serve, or one whose numbers did not add up.

Two of the three sample receipts do not decode at all, so the deterministic path is a partial
solution by measurement rather than by hope, and a miss is reported no differently from a receipt
carrying no code. The numbers, the QR's parameters and the portal's contract are recorded in
[Expenses.Infrastructure/Extraction/FISCAL-QR.md](Expenses.Infrastructure/Extraction/FISCAL-QR.md).

**Confidence is computed, not reported.** A fiscalised receipt is redundantly encoded — lines sum to
the total, list price less discount equals the paid amount, the total implies the printed VAT — so
whether the numbers were read correctly is decidable rather than estimated. That arithmetic, not a
score a model reports about its own work, is what moves an image between `Extracted` and
`NeedsReview`. A reported confidence survives only for values arithmetic cannot check: descriptions,
merchant names, category and unit guesses. `Extraction:ConfidenceThreshold` (default `0.70`) gates
those and nothing else.

Configuration:

| Key | Default | Effect |
| --- | --- | --- |
| `Extraction:ConfidenceThreshold` | `0.70` | Below this, an unverifiable value marks the image for review. |
| `Extraction:Placeholder:Outcome` | `Reconciling` | `Reconciling`, `NonReconciling`, `LowConfidence` or `Failure` — see below. |
| `Extraction:Portal:BaseAddress` | `https://mapr.tax.gov.me` | The fiscal verification service stage 2 asks. |
| `Extraction:Portal:TimeoutMilliseconds` | `5000` | Past this, retrieval produced nothing and the cascade goes on. |
| `Extraction:Vision:ModelId` | `claude-sonnet-5` | The Claude model asked to read the receipt image. |
| `Extraction:Vision:ApiKey` | *(none)* | Never committed — supplied via environment or user-secrets. Empty means the stage produces nothing, not a startup failure. |
| `Extraction:Vision:BaseAddress` | `https://api.anthropic.com` | Where the Messages API request (`/v1/messages`) is sent. |
| `Extraction:Vision:TimeoutMilliseconds` | `30000` | Past this, extraction produced nothing and the cascade goes on. |
| `TemporaryReceipts:RootPath` | `<app dir>/receipts-temp` | Where captures wait to be confirmed, distinct from the permanent receipt store. |
| `TemporaryReceipts:Sweep:Interval` | `1.00:00:00` | How often the orphan-capture sweep runs. |
| `TemporaryReceipts:Sweep:MaxAge` | `1.00:00:00` | How old an unconfirmed capture must be before the sweep removes it. |

Extraction is synchronous now: a capture or a re-run runs the cascade directly in the request, so
there is no queue and nothing to configure about draining one.

### The vision engine

`ClaudeVisionReceiptExtractor` sends the receipt image to a Claude model over the Messages API
(`/v1/messages`) as inline base64 content, with a prompt asking for one JSON object shaped like the
cascade's own candidates — line items, quantities, units, tax rate, total, merchant name, plus a
per-field confidence for the values arithmetic cannot check. A missing `Extraction:Vision:ApiKey` is
not a startup failure: the stage simply produces nothing, exactly like every other optional stage
(`Extraction:Portal:BaseAddress` being unreachable, say). Every result records the engine name and
version (the configured model id), so results from different engines, or from a fixture used in
tests, stay identifiable from one another.

`PlaceholderReceiptExtractor` performs **no image analysis** and is kept for tests that need a free,
deterministic engine: its output is derived from the content hash, so the same image always produces
the same lines and a different image produces different ones. Production wiring never selects it;
tests opt into it with the `Extraction:Vision:Engine` switch (`"placeholder"` — anything else, or
leaving it unset, resolves the real adapter).

Simulate each terminal state with `Extraction:Placeholder:Outcome` (only meaningful alongside
`Extraction:Vision:Engine: placeholder`):

- `Reconciling` — numbers that add up; the image ends `Extracted`.
- `NonReconciling` — numbers that do not add up. The failure is **real arithmetic**, not a simulated
  score, so it is reported for the reason a real engine's bad answer would be.
- `LowConfidence` — sound numbers with a description the engine is unsure of; the image ends
  `NeedsReview` with the value marked.
- `Failure` — no result at all; the image ends `Failed` with a reason, and the purchase and its
  image remain so lines can be entered by hand.

### Fiscal QR decoding: what was measured

Server-side decoding was **measured before it was specified**. A .NET spike ran ZXing over a real
photographed thermal receipt across roughly three hundred preprocessing combinations:

```
  full res                        1652 ms   miss
  downscale 0.50 / 0.35 / 0.25   150-294    miss
  bottom crop                      664 ms   miss
  tight crop, five scales           4-165   miss
  + Otsu, adaptive Otsu, dilation radius 1-3   miss
  ------------------------------------------------
  ~300 combinations                          0 hits
```

The code is not at fault and the crop is not at fault: all three finder patterns are intact. The
symbol is simply dense — roughly 69–73 modules, version 13 or 14 — printed on thermal paper where
black modules bleed together, photographed at about nine pixels per module. **This is a
signal-quality wall, not a tuning problem: do not re-derive it by adding more preprocessing.**

Stage 1 is therefore opportunistic by construction. It costs little, and when it hits — typically on
a flat, well-lit scan rather than a photograph — it yields an exactly-correct fiscal identity that
cross-checks the vision stage. Nothing depends on it hitting, and a decoder that throws is treated
as a decoder that missed.

**The stage that actually wants the QR is stage 0, in the browser, and it is the intended primary
path once `FE/` exists.** A live camera stream can autofocus, retry, and tell the user to hold
steady, which a single uploaded still cannot. The backend contract is already shaped for it: fiscal
identifiers are accepted alongside an upload, not only discovered from it.

## Connecting an MCP client

The MCP host is a second process; it never talks to the HTTP host. stdio is the default and is the
natural local-assistant experience. What the assistant can then *do* — every tool, its arguments and
what it returns — is [Expenses.Mcp/README.md](Expenses.Mcp/README.md).

```jsonc
// claude_desktop_config.json, or any MCP client's server list
{
  "mcpServers": {
    "expenses": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/Projects/expenses/BE/Expenses.Mcp"],
      "env": {
        "ConnectionStrings__Expenses": "Host=localhost;Port=5432;Database=expenses;Username=expenses;Password=expenses"
      }
    }
  }
}
```

Publish it (`dotnet publish -c Release`) and point `command` at the executable to avoid paying for a
build on every start. Nothing but protocol traffic may go to stdout, so the host logs to stderr.

For a hosted deployment, set `EXPENSES_MCP_TRANSPORT=http` and the same host serves the MCP endpoint
over HTTP instead. That is what the `mcp` compose service does — a container cannot hand its stdin
to an MCP client — so a client that speaks HTTP can point at <http://localhost:5083> with no local
build at all.

Receipt images are **not** uploadable over MCP — that is HTTP-only, and no tool accepts image bytes.
The extraction tools reference images that are already stored.

## Test-first

Every behaviour in this repository arrived with a test that was **seen to fail first** (D21). The
loop is red, green, refactor, one behaviour at a time — not three tests and then three
implementations, because the feedback between each pair is the point.

- **Spec scenarios are the test cases.** The WHEN/THEN scenarios in
  [`openspec/changes/add-personal-expense-ledger/specs/`](../openspec/changes/add-personal-expense-ledger/specs/)
  are already in the shape of test names and assertions; each test is named after the scenario it
  implements, and each test class documents the requirements it covers.
- **A test that has never failed has not been shown to test anything.** Several rules here would
  pass a vacuous test happily — a reconciliation check that never runs, a decoder-miss path never
  taken. Requiring the failing run is what distinguishes those from working code.
- **What is exempt, and why:** solution and project scaffolding, package references,
  `docker-compose.yml` and the provisioning script, EF-generated migration files, OpenAPI
  generation, and documentation. They produce no behaviour of their own; everything they enable is
  tested elsewhere. The provisioning script is exempt — the startup check asserting the database was
  provisioned correctly is not.
- **The slow suite is not an excuse to write its tests late.** `Expenses.Integration.Tests` needs
  Docker and stays red for longer than the domain tests. It is still written before the feature it
  covers.

| Project | Scope | Needs |
| --- | --- | --- |
| `Expenses.Domain.Tests` | Entities, invariants | nothing |
| `Expenses.Application.Tests` | Use cases over in-memory fakes, the arithmetic oracle, architecture rules | nothing |
| `Expenses.Integration.Tests` | EF mappings, indexes, provisioning, collation, trigram search | Docker |
