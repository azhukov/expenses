# Proposal

## Why

The ledger has no way to remove a purchase: once something is recorded it stays, and the only deletion
the system offers is detaching a receipt image. When the ledger fills with trial data (testing capture,
a first run against a new environment, a bad import) the only way back to an empty ledger is to drop the
database, which also throws away categories, units, learned unit aliases and merchants the user has built
up. A single, deliberate "clear all expenses" action gives a clean ledger while keeping that reference data.

## What Changes

- The ledger can be cleared: every purchase is deleted together with its expense lines, its fiscal
  identity, its receipt reference and any unconfirmed extraction candidates held for it. Receipt files in
  the permanent store are removed under the existing sharing rule (a file goes only when nothing still
  references it).
- Reference data is untouched: categories, units, unit aliases (including learned ones) and merchants
  survive a clear. Temporary captures awaiting confirmation are also untouched and can still be confirmed
  afterwards.
- Clearing is guarded on the server: the request must state how many purchases the caller expects to
  delete. A request without that value is rejected; a request whose value no longer matches the ledger
  (something was recorded or cleared in the meantime) is rejected and reports the current count. In
  either case nothing is deleted.
- The ledger reports how many purchases it holds, so a client can show that number before asking for
  confirmation.
- HTTP gains an endpoint to count purchases and an endpoint to clear them. MCP gains the read-only count
  tool but **not** a clear tool: clearing is deliberately HTTP-only so an assistant cannot wipe the
  ledger. This is a new, named exception to the rule that both interfaces expose the same behaviour.
- The browser client gains a quiet "Clear all" control on the home screen. Activating it shows how many
  purchases will be deleted and asks for explicit confirmation; confirming clears the ledger and the home
  screen then shows the empty-ledger state. Cancelling changes nothing.

## Capabilities

### New Capabilities

_None._ Clearing is an operation on purchases and belongs with the existing capabilities below.

### Modified Capabilities

- `purchase-recording`: adds the requirement that the ledger can be cleared of all purchases (what is
  removed, what is kept, the expected-count guard, and that the duplicate guard no longer sees cleared
  purchases), and that the number of recorded purchases can be read.
- `receipt-ingestion`: states that clearing the ledger disposes of receipt files under the existing
  sharing rule and leaves temporary captures available for confirmation.
- `api-surface`: adds clearing to the HTTP interface, adds counting to both interfaces, and extends the
  parity requirement's list of HTTP-only operations with clearing the ledger.
- `browser-client`: adds the "Clear all" control on the home screen with a confirmation step that shows
  the number of purchases to be deleted.

## Impact

- **Backend**: `PurchaseService` gains count and clear use cases; `IPurchaseRepository` gains a count and a
  bulk delete returning the receipt storage keys it removed; `PurchasesController` gains
  `GET /purchases/count` and `DELETE /purchases?expectedCount=N`; MCP gains a `count_purchases` tool. New
  application error codes for a missing and a stale expected count. No schema migration: expense lines
  already cascade from purchases, and receipt and fiscal data live on the purchase row.
- **Frontend**: new API calls in `api/reads.ts` / `api/writes.ts`, a confirmation component, a control on
  `Home.tsx`, and query invalidation so the month view refreshes after a clear.
- **Data**: clearing is irreversible. No backup or undo is provided.
- **Cross-origin**: `DELETE` is already an allowed method, so no CORS change is required.
