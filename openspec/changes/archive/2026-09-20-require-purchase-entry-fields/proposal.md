## Why

The review screen lets a capture be confirmed with values the ledger will refuse or record meaninglessly: an empty description, a missing amount, no lines at all, a date the user cleared. Today the only answer is the API's rejection, which arrives after a round trip, names one problem at a time, and points at no particular input — so the user has to guess which of six lines the message is about. The rules the ledger actually enforces belong in front of the user, on the field that breaks them.

The screen is also silent about where its numbers came from. Extraction runs a named engine, the response reports it (`ExtractionResultView.engineName`), and the person deciding whether to trust a line has no way to see which reading they are correcting.

## What Changes

- **A purchase cannot be confirmed without a date, a total amount and at least one expense line.** The merchant stays optional, as it is today.
- **An expense line requires a description of at most 200 characters, an amount, a quantity and a unit.** The category stays optional.
- **BREAKING (API): a unit is now required on every expense line.** `POST /purchases` and the MCP `record_purchase` and `confirm_capture` tools reject a line that resolves to no unit — neither a `unitCode` nor a unit matched during extraction. Confirming an extraction whose candidate lines matched no unit now fails unless the caller supplies one; the browser client always will, because its form requires it.
- **BREAKING (API): a description longer than 200 characters is rejected** rather than stored.
- **Invalid fields are highlighted on the review screen.** Confirm does not call the ledger while anything is invalid; every offending input is marked at once, each with its own message, and a field's mark clears as soon as its value becomes valid.
- **The review screen names the extraction engine at the top of the screen**, from the capture response's own `engineName`, and says plainly when a capture failed and produced no engine reading.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `purchase-recording`: "Expense line detail" changes from a unit being optional to a unit being required on every expense line, and gains a maximum length for a description. The rule is interface-agnostic and so covers the HTTP interface and the MCP tools alike.
- `browser-client`: adds a requirement that the review screen refuses to submit an incomplete purchase and marks the inputs at fault, and a requirement that it names the extraction engine that produced what is being reviewed.

## Impact

- **FE code:** `src/routes/CaptureReview.tsx` and `CaptureReview.module.css` (error marks, the engine name, a submit that validates first), `src/capture/review.ts` (the validation rules, as functions testable without rendering), plus unit tests for both and the `e2e/` capture flow.
- **API code:** `Expenses.Domain/Entities/Expense.cs` (description length; a required unit id), `Expenses.Application/Services/ExpenseAssembly.cs` (refuse a line that resolves to no unit, naming the line), `Expenses.Application/Errors/ApplicationErrors.cs`, and `Expenses.Application/Services/ReceiptService.cs`, whose `AsCommand` currently passes `UnitCode: null` for every candidate. `Expenses.Api/ExpenseRequest.cs` and `Expenses.Mcp/Tools/ExpenseArgument.cs` document the unit as required while leaving it optional in the shape itself, so a missing unit is refused by the ledger with a message naming the line rather than by request binding.
- **Existing data is untouched.** The rule applies to newly recorded expenses; expenses already stored without a unit stay readable, the same way a deactivated category stays readable on the lines that reference it.
- **Flagged, not in this change: the Avalonia desktop client.** It records purchases through the same endpoint (`Expenses.Desktop.Core/Ledger/ExpenseRequest.cs`, `Rules/EditableLine.cs`) and does not require a unit in its own form, so after this change a unit-less line there will be rejected by the ledger and shown as the API's error. Bringing its form to parity is a separate change and should follow this one.
- **No new dependencies.**
