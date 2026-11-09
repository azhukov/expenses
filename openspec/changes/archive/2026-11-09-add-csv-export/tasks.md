# Tasks

## 1. Ports and DTO (exempt from test-first: signatures only; behaviour is covered in 2.0 and 3.0)

- [x] 1.1 Add `ListForExport(DateOnly? from, DateOnly? to, CancellationToken)` to `IPurchaseRepository` (design D2), with an XML doc stating oldest-first order and inclusive bounds. Give the EF implementation a `throw new NotImplementedException()` body, and implement it in the `InMemoryLedger` fake (oldest first, then by id). Verify with `dotnet build BE/Expenses.sln`.
- [x] 1.2 Add the `ExportLine` record to `Dtos/Purchases` (design D1), with fields in the spec's column order. Add `PurchaseService.Export(DateOnly? from, DateOnly? to, CancellationToken)` as a `NotImplementedException` stub. Verify with `dotnet build BE/Expenses.sln`.

## 2. Export use case

- [x] 2.0 Write failing tests over fakes in a new `Expenses.Application.Tests/Purchases/ExportLedgerTests`, and run them to see each fail:
  - Range (ledger-export): "Export everything", "Export one month", "Only a start is given", "Nothing in the range" (empty list), "An export is not paged" (more than `MaxPageSize` purchases).
  - "An inverted range is rejected": code `listing.range_invalid`, fields `from` and `to`, and the same message `List` gives for the same input.
  - "A purchase with three lines": three `ExportLine`s carrying the purchase id, with amounts summing to the purchase amount.
  - "Optional values that are absent": null unit price, discount and category.
  - Naming: "Matched references are named" (merchant name, category name, unit symbol), "Unmatched references fall back to what was printed", "A deactivated merchant is still named" (and a deactivated category), "Neither a reference nor verbatim text".
  - Ordering: "Oldest first", "Lines keep their recorded order", and two purchases at the same moment come out in id order.
- [x] 2.1 Lift the inverted-range check out of `PurchaseService.List` into a private helper shared with `Export`. Verify that the existing `PurchaseQueryTests` still pass unchanged.
- [x] 2.2 Implement `PurchaseService.Export` per design D1: one load of each name dictionary with inactive entries included, then flatten with lines ordered by id. Verify that every 2.0 test passes.

## 3. Persistence

- [x] 3.0 Write failing integration tests in a new `Expenses.Integration.Tests/Persistence/PurchaseExportStorageTests` against the Testcontainers database, and run them to see each fail:
  - `ListForExport` with no bounds returns every purchase, oldest first then by id, with expenses loaded.
  - "The last day is included whole": a purchase at `2026-10-31 23:59:30` is returned for `to = 2026-10-31`, and one at `2026-11-01 00:00:00` is not.
  - A start alone and an end alone each bound correctly.
  - More than 200 purchases are all returned.
- [x] 3.1 Factor the range predicate in `PurchaseRepository` into a private helper shared by `List` and `ListForExport`, then implement `ListForExport` with `AsNoTracking()` and ascending order (design D2). Verify that every 3.0 test passes and the existing listing storage tests still pass.

## 4. CSV encoding

- [x] 4.0 Write failing unit tests for `LedgerCsv.Write` in `Expenses.Integration.Tests/Api/LedgerCsvTests` (the API project has no test project of its own), and run them to see each fail:
  - "The header row": exact header, preceded by a UTF-8 BOM, with CRLF row endings.
  - "Text with commas and quotes survives": `Juice "Orange", 1L` is written as `"Juice ""Orange"", 1L"`. Round-trip through a minimal RFC 4180 parser in the test.
  - A field containing a line break is quoted.
  - "Non-Latin text survives": `Hljeb, čokolada, Ćevapi`.
  - "Numbers use an invariant form": `1.25`, `3.20`, `4.00`. Run the test under `CultureInfo("sr-Latn-ME")`, `de-DE` and `fr-FR` to prove culture independence.
  - "Occurrence is written as recorded" (`2026-10-14 13:05:00`), and "A purchase recorded by date alone" (`… 00:00:00`).
  - Null optional fields are written empty.
- [x] 4.1 Implement `Expenses.Api/LedgerCsv.cs` per design D3, with no package reference. Verify that every 4.0 test passes.

## 5. HTTP and MCP

- [x] 5.0 Write failing tests in a new `Expenses.Integration.Tests/Api/LedgerExportTests`, and run them to see each fail:
  - "Exporting everything over HTTP": 200, `text/csv; charset=utf-8`, `Content-Disposition` attachment `expenses-all.csv`, and a body whose rows match the arranged purchases.
  - "Exporting a range over HTTP": `expenses-2026-10-01-to-2026-10-31.csv`, with only October's lines.
  - "An open-ended range is named for its open end": `…-2026-10-15-to-latest.csv`, and `expenses-earliest-to-2026-10-31.csv` for an end alone.
  - "An inverted range over HTTP": 400, `application/json` in the standard shape, code `listing.range_invalid`.
  - "A malformed date over HTTP": 400 in the validation shape, naming `from`.
  - "Exporting from an allowed browser origin": a GET with `Origin` gets `Access-Control-Allow-Origin` back.
  - Read-only: the purchase count is unchanged after an export.
  - `Mcp/McpAdapterTests`, "Exporting is not offered over MCP": no tool name contains `export`, and `list_purchases` is still listed.
- [x] 5.1 Add `GET /purchases/export` to `PurchasesController` per design D4: `[Produces("text/csv")]`, `DateOnly?` bindings, the file-name rule, and `File(...)`. Verify that every 5.0 test passes.

## 6. Browser client

- [x] 6.0 Write failing Vitest and Testing Library tests, and run them to see each fail:
  - `month/month.test.ts`: `monthRange(2026, 9)` gives `2026-09-01` to `2026-09-30`, including a February in a leap year. `previousMonth`/`nextMonth` handle the year boundary. `exportFileName` gives `expenses-all.csv`, `expenses-2026-10.csv` and `expenses-2026-10-05-to-2026-10-12.csv` ("Files are named for what was chosen").
  - `api/client.test.ts` / `api/reads.test.ts`: `exportPurchases()` requests `/purchases/export` with no query and `accept: text/csv`, and with a range sends `from` and `to`. It resolves to a Blob whose bytes equal the body ("The file is the ledger's file"). A JSON error body becomes a `LedgerError` carrying the code, and a network failure becomes "The ledger could not be reached."
  - New `components/ExportDialog.test.tsx`, with an injected `save` spy:
    - "The current month is the default", with a fixed clock in October 2026, and the › stepper disabled.
    - "Exporting a month": step back to September, confirm, and the request is September's range with `save` called as `expenses-2026-09.csv`.
    - "Exporting everything" and "Exporting a date range".
    - "An incomplete range cannot be sent" (Export disabled).
    - "An inverted range is refused before sending" (`aria-invalid` on both inputs, message shown, no request sent).
    - "An export in progress" (pending text, Export disabled until the request resolves).
    - The dialog closes after a successful save.
    - "The ledger rejects the export" (the ledger's message shown, `save` not called, range still selected).
    - "The ledger cannot be reached" (message shown, can confirm again).
    - "Cancelling sends nothing".
  - `routes/Home.test.tsx`: "The control is reachable from home", and "Activating the control downloads nothing" (the dialog is shown and no export request is sent). The prominence test now expects capture plus three quiet tools. The existing `Home.layout.test.tsx` touch-target test covers the new button.
- [x] 6.1 Add the month helpers and `exportFileName` to `month/month.ts`. Verify that the `month.test.ts` cases from 6.0 pass.
- [x] 6.2 Add `readBlob` to `api/client.ts` and `exportPurchases` to `api/reads.ts` (design D6). Verify that the api tests from 6.0 pass.
- [x] 6.3 Add `components/ExportDialog.tsx` and its CSS module per design D6, with the default `save` implementation (object URL, temporary `<a download>`, revoke). Verify that the dialog tests from 6.0 pass.
- [x] 6.4 Add the "Export" button to Home's `tools` nav before "Clear all", owning the dialog's open state. Verify that the Home tests from 6.0 pass.
- [x] 6.5 Run the `fe-style-fix` workflow (format, lint:fix, tsc, structure check) until it is clean. Verify that `npm run lint` and the structure check exit 0.

## 7. Wrap-up (exempt from test-first: style, documentation and verification)

- [x] 7.1 Run the `be-editorconfig-fix` workflow until `dotnet build BE/Expenses.sln` is clean under `TreatWarningsAsErrors`.
- [x] 7.2 Run all BE test projects and the FE suite (`./test-be.sh`, `./test-fe.sh`) and record the actual pass counts. (Domain 100; Application 249; Integration 261 passed, 1 skipped as manual-only; FE 272.)
- [x] 7.3 Update `BE/tests/SCENARIO-COVERAGE.md` with every scenario in this change's specs and where it is tested. Add the HTTP-only export exception to `BE/Expenses.Mcp/README.md`, an "Exporting the ledger" section to `BE/README.md` (endpoint, parameters, dialect), and a request to `Expenses.Api.http`. Verify that every scenario is listed.
- [x] 7.4 Manual check against the running app (`./up.sh`): record purchases in two months, including a description with a comma and a Cyrillic or `č` character. Export everything, the current month and a range from Home. Open each file in a spreadsheet, check the rows, names and characters, and check that `amount` sums to the month total shown on Home. (Done by the user against the running app.)

## Workflow follow-up

- Sync the delta specs and archive the change once review is complete (`/opsx:archive`).
