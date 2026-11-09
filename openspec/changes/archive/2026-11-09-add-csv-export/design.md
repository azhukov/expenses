# Design

## Context

See proposal.md for why, and `specs/` for the observable contract. The state this design works from:

- `PurchaseService.List(from, to, skip, take)` already validates an inverted range
  (`listing.range_invalid`, fields `from` and `to`). It also caps a page at `MaxPageSize = 200`.
  `PurchaseRepository.List` turns the inclusive `to` into an exclusive next-midnight instant and orders
  most recent first. `Purchase.Expenses` is auto-included, so a range read brings its lines with it.
- A purchase references a merchant, and its lines reference a category and a unit, by nullable
  identifier. The verbatim `*Raw` text sits beside each identifier (D9). `Merchant.Name`,
  `Category.Name` and `Unit.Symbol` (e.g. `kg`) are the human names. Each reference repository has
  `List(includeInactive)`. `PurchaseService` already holds the category and unit repositories, and
  holds `MerchantService` rather than the merchant repository.
- `OccurredAt` is wall-clock, `DateTimeKind.Unspecified`, and never converted (D5).
- The Application project carries no package references. The API project maps `ExpensesException` to
  the single JSON error shape in `ErrorHandling`, with codes not listed elsewhere mapping to 400.
- CORS allows `GET` with the `accept` and `content-type` request headers, and exposes no response
  headers beyond the defaults.
- FE: `api/client.ts` has `read<T>` (JSON) and `failureOf(response)`, which turns an error body into
  a `LedgerError`. `month/month.ts` has `currentMonthRange()`. `structure.config.json` ranks
  `api`/`format` 0, `month` 1, `components` 2 and `routes` 3. Home's `tools` nav holds "Units" and
  "Clear all". `ClearLedgerDialog` is the established pattern for a modal tool with local pending
  and failure state.

## Goals / Non-Goals

**Goals:**

- One export use case behind one endpoint, with the range rules shared with the listing, not copied.
- The row shape (what a row is, its columns, its names) is decided in Application. The CSV encoding is
  a separate, replaceable concern.
- A download that reports failure in the dialog in the ledger's words, as every other client call does.

**Non-Goals:**

- Streaming the response row by row. The ledger is personal-sized (thousands of lines), so a fully
  built body is simpler and lets a failure become a proper JSON error before any byte is sent.
- Escaping spreadsheet formulas (see Risks).
- An MCP tool, a preview count, or a file name chosen by the user.

## Decisions

### D1: `PurchaseService.Export` returns rows, not CSV

`PurchaseService` gains `Export(DateOnly? from, DateOnly? to)`, which returns
`IReadOnlyList<ExportLine>`. `ExportLine` is a record in `Dtos/Purchases` whose fields match the spec's
columns one for one: `long PurchaseId`, `DateTime OccurredAt`, `string? Merchant`, `string Description`,
`decimal Quantity`, `string? Unit`, `decimal? UnitPrice`, `decimal? ListUnitPrice`,
`decimal? DiscountAmount`, `string? Category`, `decimal Amount`.

1. Reject an inverted range with `listing.range_invalid`. The check is lifted out of `List` into a
   private helper that both methods call, so the message and fields stay identical (spec: "with the
   error the listing reports").
2. Read the range with the new repository method (D2).
3. Load the name dictionaries once, inactive entries included: categories (id → Name), units
   (id → Symbol) and merchants (id → Name, through `MerchantService`'s existing list including inactive
   merchants). That is three queries regardless of row count, so there is no N+1 problem.
4. Flatten each purchase into one line per expense, in recorded order (D2). Resolve each name as
   `dictionary[id] ?? raw ?? null`.

Rows rather than bytes, because "a row is an expense line, and it is named, not identified" is a
ledger rule, and Application is where rules live. Encoding is presentation.

*Alternative considered:* return `PurchaseView`s and flatten in the controller. Rejected because it
puts a rule (name resolution and its fallback) in an adapter, which is the D1 violation the controller
comments warn against.

### D2: An unpaged, oldest-first range read

`IPurchaseRepository` gains
`Task<IReadOnlyList<Purchase>> ListForExport(DateOnly? from, DateOnly? to, CancellationToken)`.

- The range predicate is the same as `List`. It is factored into a private `InRange(query, from, to)`
  helper in `PurchaseRepository`, so the inclusive-last-day rule exists once.
- The order is `OccurredAt` ascending, then `Id` ascending. Same-moment purchases therefore come out in
  recorded order, because ids are never reused.
- Lines come out in recorded order: the auto-include does not guarantee order, so the service orders
  each purchase's `Expenses` by `Id` when flattening.
- The read uses `AsNoTracking()`, because nothing is written.

*Alternative considered:* calling `List` with `take = int.MaxValue`. Rejected because it would
bypass the page cap the service enforces for a reason, and it gives the wrong order.

### D3: A hand-written CSV writer in the API project

`Expenses.Api` gains `LedgerCsv` (static), with `byte[] Write(IReadOnlyList<ExportLine>)`. The writer
implements the spec's dialect directly:

- A UTF-8 byte-order mark, a fixed header row, and CRLF row endings.
- A field is quoted only if it contains `,`, `"`, CR or LF. Inner quotes are doubled.
- Money (`UnitPrice`, `ListUnitPrice`, `DiscountAmount`, `Amount`) is formatted with
  `ToString("0.00", InvariantCulture)`. `Quantity` is formatted with `ToString("0.###", InvariantCulture)`,
  which drops trailing zeros. *Changed during apply:* the first plan was to keep the scale "as
  recorded". But the `quantity` column has a fixed scale of 3, so a stored `1` reads back as `1.000`
  and the scale it was supplied with cannot be recovered. A quantity carries at most 3 places (D10),
  so `0.###` never drops a significant digit.
- `OccurredAt` is formatted with `ToString("yyyy-MM-dd HH:mm:ss", InvariantCulture)`. It is
  `Unspecified`, so formatting does no conversion.
- A null field is written as empty.

There is no package dependency. The dialect is about 40 lines, and is pinned by the spec's scenarios.
A library would bring its own culture and quoting defaults, which the tests would then have to argue
with.

*Alternative considered:* CsvHelper. Rejected for the reason above. It also has no reader on the other
side of this change yet that would justify the dependency.

### D4: The HTTP shape

`GET /purchases/export?from=YYYY-MM-DD&to=YYYY-MM-DD` on `PurchasesController`, with both parameters
optional and bound as `DateOnly?`.

- On success, it returns `File(bytes, "text/csv; charset=utf-8", fileName)`, which sets
  `Content-Disposition: attachment`. The success response is described with
  `[ProducesResponseType(typeof(FileContentResult), 200, "text/csv")]`. The controller-level
  `[Produces("application/json")]` stays in force: a file result bypasses output formatters, so it
  does not affect the CSV. *Changed during apply:* the action first carried
  `[Produces("text/csv")]`. That forced a model-binding failure (e.g. `from=yesterday`) to be
  negotiated as CSV, and with no CSV formatter MVC answered 406 instead of the 400 validation error.
- The file name is derived in the controller. It is a presentation detail of HTTP, not a ledger rule:
  - `expenses-all.csv` when neither bound is given.
  - `expenses-{from}-to-{to}.csv` otherwise, with `earliest` standing in for a missing start and
    `latest` for a missing end.
- The literal `export` segment cannot collide with `{id:long}`.
- An inverted range throws before the body is built, so the existing exception middleware writes the
  standard JSON error with status 400. A malformed date fails model binding and is reported in the
  existing validation shape.

### D5: No MCP tool, and the exception is asserted

No tool calls `Export`. `McpAdapterTests` gains an assertion that no listed tool name contains
`export`, and the parity requirement names the exception (see the api-surface delta). An assistant
answers range questions through `list_purchases`, which already returns everything the file holds, in
a shape it can reason about.

### D6: Client: fetch to a Blob, then save it

- `api/reads.ts` gains `exportPurchases(range?: { from: string; to: string }): Promise<Blob>`. It uses
  a small sibling of `read` in `client.ts`, `readBlob(path)`, with `accept: 'text/csv'`. It shares
  `ledgerUrl`, the "could not be reached" message and `failureOf`, so error handling is identical to
  JSON reads.
- It fetches rather than navigating to the URL (`<a href>`) for two reasons. A navigation that fails
  would show raw JSON in a new page instead of a message in the dialog. And the `download` attribute
  is ignored cross-origin, while the ledger is on another origin.
- The client names the file itself (`expenses-all.csv`, `expenses-YYYY-MM.csv`,
  `expenses-FROM-to-TO.csv`). It can name a month, which the server cannot see. It also avoids exposing
  `Content-Disposition` through CORS. The server's names remain the contract for direct HTTP callers.
- `month/month.ts` gains pure helpers: `monthRange(year, month)`, `previousMonth`/`nextMonth`, and
  `exportFileName(choice)`. They live with the month logic and are unit-testable without a DOM.
- `components/ExportDialog.tsx` (rank 2) follows the `ClearLedgerDialog` pattern: `role="dialog"`,
  `aria-modal`, a labelled heading, local `isPending`/`failure` state, and Cancel and Export buttons.
  - Three radio choices: Everything, Month (default) and Range. The month is shown as "October 2026"
    between ‹ › steppers, and › is disabled at the current month. The range is two
    `<input type="date">`.
  - Export is disabled while pending or while the range is incomplete. An inverted range marks both
    inputs `aria-invalid` and shows the message.
  - Saving happens through a `save(blob, fileName)` prop, whose default creates an object URL, clicks a
    temporary `<a download>`, and revokes the URL. The prop is the seam that lets tests assert what was
    saved under jsdom, which cannot download.
- `Home.tsx` adds "Export" to the `tools` nav, before "Clear all", and owns the dialog's open state,
  as it does for clearing.

*Alternative considered:* `<input type="month">` for the month. Rejected because desktop Firefox
renders it as a free-text field, so the default and the "not past this month" rule could not be
relied on. The stepper behaves the same everywhere, and moving one month back is the common case.

## Risks / Trade-offs

- **[Formula injection]** A description or merchant text beginning with `=`, `+`, `-` or `@` is
  evaluated by Excel as a formula. → This is accepted and not escaped. The text comes from the user's
  own receipts. Legitimate lines start with `-` ("-20% popust"). And prefixing `'` would change the
  data, which the spec forbids. Revisit this if exports are ever shared beyond the owner.
- **[Excel in a comma-decimal locale]** Opening the file by double-click may split or misread numbers.
  → The dialect was chosen for portability (Sheets, LibreOffice, scripts, a later import). Excel users
  can use Data › From Text with comma and `.` settings. A locale-specific dialect is a possible later
  option, not a change to this contract.
- **[Memory for a very large export]** The body is built fully before sending. → A personal ledger is
  thousands of lines, i.e. kilobytes to a few megabytes. If that changes, D3's writer can move to
  streaming without changing the spec.
- **[Unauthenticated endpoint]** Anyone who can reach the HTTP interface can read the whole ledger in
  one request. → This is the same exposure `GET /purchases` already has, page by page. It is another
  reason to add authentication before any shared deployment.
- **[Blob download on iOS Safari]** Older versions opened blobs in a tab instead of saving them. →
  Supported versions honour `<a download>` on object URLs. The worst case is that the CSV opens for
  viewing, from where it can still be shared or saved.

## Migration Plan

No schema migration and no configuration change. Deploy the backend and frontend together as usual.
Rolling back removes the endpoint and the button. Nothing is stored, so there is nothing to undo.
