# Proposal

## Why

The ledger can only be read one page of purchases at a time, through the client or an assistant.
There is no way to take the data somewhere else, such as a spreadsheet, a budget review or a backup
before a clear. The obvious slices a user wants are everything, one month, or a chosen date range, and
the listing already filters by exactly that. What is missing is a single download in a format any
spreadsheet opens.

## What Changes

- The ledger can be exported as a CSV file covering all purchases, or the purchases in an inclusive
  occurrence date range. A month is a date range from its first to its last day, so the three modes
  are one operation.
- The file has **one row per expense line**. Each row carries its purchase's identifier, occurrence and
  merchant. Line amounts are authoritative and reconcile to their purchase, so summing the `amount`
  column gives the correct total for any slice. The purchase total is deliberately not repeated on
  every line, where summing it would overcount.
- Merchants, categories and units appear by name, never by identifier. Where a reference was never
  matched, the verbatim receipt text is exported instead.
- The CSV dialect is fixed so the file opens the same way everywhere: UTF-8 with a byte-order mark,
  comma-separated, RFC 4180 quoting, invariant `.` decimals, and ISO dates with no timezone conversion.
- Rows are ordered oldest first, as a spreadsheet reads. Lines within a purchase keep the order they
  were recorded in.
- HTTP gains `GET /purchases/export` returning `text/csv` as an attachment. Export is **HTTP-only**: an
  assistant already reads purchases through the listing tool, and has no use for a file. This is added
  to the named exceptions to the parity rule, as clearing was.
- The browser client gains a quiet "Export" tool on the home screen. It opens a dialog to choose
  everything, a month (the current month by default) or a date range, and downloads the file. A
  failure is reported in the dialog in the ledger's own words.

Not part of this change: importing CSV, other formats (XLSX, JSON), choosing columns or dialect, and
a preview count before download. Export first, in a format that a later import could read back.

## Capabilities

### New Capabilities

- `ledger-export`: what an export of the ledger contains and how it is encoded. This covers the range
  it covers, one row per expense line, the columns and how references are named, ordering, and the CSV
  dialect. Further export formats, or a later import of the same format, belong here.

### Modified Capabilities

- `api-surface`: adds the HTTP export endpoint, and extends the parity requirement's list of HTTP-only
  operations with exporting the ledger.
- `browser-client`: adds the "Export" tool on the home screen, the dialog that chooses the range, and
  how failures are reported in it.

## Impact

- **Backend**:
  - `PurchaseService` gains an `Export(from?, to?)` use case. It validates the range with the
    listing's existing error and resolves names from the category, unit and merchant repositories.
  - `IPurchaseRepository` gains an unpaged, oldest-first range read.
  - `PurchasesController` gains `GET /purchases/export?from=&to=`, and a CSV writer in the API project
    encodes the rows.
  - There is no new MCP tool, no schema migration, and no new package.
- **Frontend**:
  - `api/reads.ts` gains a download call that turns an error body into `LedgerError`.
  - A new `ExportDialog` component, and a tool button on `Home.tsx`.
  - Small date helpers alongside `month/month.ts`.
- **Cross-origin**: `GET` and the `accept` header are already allowed. The client names the file
  itself, so no response header needs exposing.
- **Data**: read-only. Nothing in the ledger changes.
