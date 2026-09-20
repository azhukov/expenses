## 1. Domain: a description has a maximum length

- [x] 1.0 In `BE/tests/Expenses.Domain.Tests/ExpenseTests.cs`, write failing cases for the purchase-recording scenarios "A description at the limit is accepted" (200 characters, stored whole) and "An over-long description is rejected" (201 characters throws, and nothing is truncated). Add a case proving the limit is applied after trimming, so 200 characters padded with spaces is accepted.
- [x] 1.1 In `Expense.Record`, add the 200-character limit beside the existing non-empty description check, throwing the same kind of exception its neighbours do (D5). 1.0 goes green.
- [x] 1.2 Add `ExpenseDescriptionTooLong = "expense.description_too_long"` to `ApplicationErrors` and map the new exception in `DomainErrorTranslation.Expense`. Write the failing application-level case first, in `BE/tests/Expenses.Application.Tests/Purchases/RecordPurchaseTests.cs`: recording a purchase with a 201-character line fails with that code and a message a person can read.

## 2. Application: an expense line requires a unit

- [x] 2.0 In `RecordPurchaseTests.cs`, write failing cases for the purchase-recording scenarios "An expense without a unit is rejected" (no `UnitCode`, no matched unit → `expense.unit_required`, the error's fields name the line) and "A unit matched during extraction satisfies the rule" (a command carrying only `MatchedUnitId` is accepted with that unit). Cover a multi-line purchase where only the second line lacks a unit, and assert the reported line is the second.
- [x] 2.1 Add `ExpenseUnitRequired = "expense.unit_required"` to `ApplicationErrors`. In `ExpenseAssembly.Build`, after both `ResolveUnit` and `MatchedUnitId` have been consulted, refuse a line that has neither and name its position (D5). 2.0 goes green.
- [x] 2.2 Write a failing integration test for the same rule over the HTTP interface, in `BE/tests/Expenses.Integration.Tests/`, so the `ErrorResponse` shape the browser client reads is proved: `POST /purchases` with a unit-less line returns the code, a readable message and the line in `fields`.
- [x] 2.3 In `Expenses.Api/ExpenseRequest.cs` and `Expenses.Mcp/Tools/ExpenseArgument.cs`, describe `UnitCode` as required where a caller reads it. Making it a required parameter was tried and reverted (D8): binding then fails before the use case runs and the client gets `request_invalid` instead of the error naming the line. The behaviour is covered by 2.0–2.2.
- [x] 2.4 Write a failing test for the confirm-capture path (D6): confirming a capture with no edited lines, where a candidate matched no unit, fails with `expense.unit_required` naming the line; where every candidate matched a unit, it still succeeds. Then update `ReceiptService.AsCommand`'s comment to say why it no longer passes a unit silently — no default is invented.
- [x] 2.5 Record the new scenarios in `BE/tests/SCENARIO-COVERAGE.md` and run the `be-editorconfig-fix` checks until the BE build is clean. Documentation and tooling, no test.

## 3. FE: the validation rules

- [x] 3.0 Add `FE/src/capture/review.test.ts` (new file) with failing cases for a `validate(edits)` function (D1), taken from the browser-client scenarios:
  - "Confirming with nothing filled in is refused": empty date, empty amount and one empty line produce an error for each required value.
  - "Every offending value is reported at once": two invalid lines both appear in the result, and both of a line's faults are reported together.
  - "A purchase with no lines is refused": an empty line list produces the list-level error.
  - "An over-long description is refused": 200 characters is valid, 201 is not, measured after trimming.
  - "A missing unit is refused": an empty `unitCode` is an error; an empty `categoryCode` is not.
  - "An optional value left empty does not block confirmation": an empty merchant and no categories validate clean.
  - "A date from the receipt satisfies the requirement": a capture with a fiscal date and an untouched date field validates clean.
  - Amount and quantity: blank, non-numeric and negative are each errors; `0` and a fractional quantity are not.
- [x] 3.1 Implement `validate` in `FE/src/capture/review.ts`, returning every error keyed by field and by line key (D1). Comment the 200-character constant with the domain rule it mirrors (D2). 3.0 goes green.

## 4. FE: the review screen marks what is wrong

- [x] 4.0 Add `FE/src/routes/CaptureReview.test.tsx` (new file) with failing cases for:
  - "The faulty input is marked": confirming with a blank line amount marks that input and shows its message, leaves the valid inputs unmarked, and `onConfirm` is not called.
  - "A correction clears the mark": typing a valid amount into a marked input removes its mark and message without confirming again (D3).
  - "A marked input is announced as invalid": the input carries `aria-invalid` and is described by its message element (D4).
  - "Confirming with nothing filled in is refused" and "A missing date never reaches the ledger", asserted at the screen level: no confirmation is submitted, and the date input is marked.
  - Nothing is marked before the first confirm attempt.
- [x] 4.1 In `CaptureReview.tsx`, hold the `attempted` flag, derive the errors from the current values on each render once it is set, and block `onConfirm` while any exist (D3). Render each field's message element with a stable id, wire `aria-invalid` and `aria-describedby`, and add `data-testid` attributes following the screen's existing convention. 4.0 goes green.
- [x] 4.2 In `CaptureReview.module.css`, style the invalid state as a border weight plus the message, never colour alone, and reserve the message's space so marking a line does not reflow the ones below it (D4, and the reflow risk in design.md).

## 5. FE: the extraction engine is named

- [x] 5.0 In `CaptureReview.test.tsx`, write failing cases for the browser-client scenarios "The engine that produced the reading is named" (the response's `engineName` is shown above the fields) and "A failed capture states that there is no reading" (a capture with a null `result` says so and shows no name).
- [x] 5.1 Render the engine name in the screen's header from `capture.result?.engineName` alone, with no fallback or inference (D7). 5.0 goes green.

## 6. End to end

- [x] 6.0 Extend `FE/e2e/capture.spec.ts` with a run that attempts to confirm an incomplete capture, sees the marks, corrects the values and confirms successfully — proving the client's rules and the ledger's agree in a running system. Run `./test-e2e.sh`.
- [x] 6.1 Run `./test-be.sh` and `./test-fe.sh`, and the `fe-style-fix` checks until `npm run style` is clean. Report which suites ran and what they returned. Tooling, no test.
- [x] 6.2 Note in the proposal's flagged item, or in a short follow-up change, that the Avalonia client's own form still does not require a unit. Documentation, no test.
