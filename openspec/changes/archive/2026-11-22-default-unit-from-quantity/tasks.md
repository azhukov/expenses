# Tasks

## 1. Domain: an inferred unit on a candidate

- [x] 1.0 Write failing tests in `BE/tests/Expenses.Domain.Tests/ExtractionCandidateTests.cs`: `InferUnit` sets the unit and marks it inferred, and `MatchUnit` sets the unit without marking it. Run them and see them fail to compile or fail on the assertion.
- [x] 1.1 Add `UnitInferred` and `InferUnit(long unitId)` to `ExtractionCandidate` (design D2). Verify that 1.0 passes.

## 2. Application: inference in `MatchUnits`

- [x] 2.0 Write failing tests in `BE/tests/Expenses.Application.Tests/Receipts/UnitMatchingTests.cs`, one per receipt-ingestion scenario: "Unknown text leaves the line unmatched" (Pfand, 2 → PCS inferred), "Unknown text with a fractional quantity is inferred as kilograms", "A line with no printed unit is inferred", "An absent quantity is inferred as pieces", "A whole quantity written with decimals is a whole number", "An inactive unit is not inferred", "A re-run uses the aliases as they now stand" (now KD → PACK, not inferred), and the fiscal "units are matched" scenario asserting that no line is inferred. Run them and see the inference ones fail.
- [x] 2.1 Widen the `MatchUnits` filter to candidates without a unit, and on a lookup miss infer PCS or KG from `quantity ?? 1` among active units (design D1). Verify that 2.0 passes and the existing `UnitMatchingTests` still pass.

## 3. Application: inferred units teach no alias

- [x] 3.0 Write failing tests in `BE/tests/Expenses.Application.Tests/Purchases/UnitLearningTests.cs` for the purchase-recording scenarios: "An inferred unit confirmed untouched teaches nothing", "An inferred unit confirmed unedited from candidates teaches nothing", "A unit chosen in place of an inferred one is learned", and "Marking a unit as inferred does not supply one". Also cover receipt-ingestion "A receipt with inferred units can be confirmed unchanged". Run them and see them fail.
- [x] 3.1 Add `UnitInferred` (default false) to `ExpenseCommand`, pass the candidate's flag in `ReceiptService.AsCommand`, and skip alias learning in `ExpenseAssembly` when it is set (design D3). Rewrite the `AsCommand` rationale comment to match. Verify that 3.0 passes and the existing `UnitLearningTests` still pass.

## 4. API: the flag on the wire

- [x] 4.0 Write failing tests in `BE/tests/Expenses.Integration.Tests/Api/HttpAdapterTests.cs`: a capture response's candidate exposes `unitInferred`, and a confirm request carrying `unitInferred: true` with unknown printed text records the expense without creating the alias. Run them and see them fail.
- [x] 4.1 Expose `UnitInferred` on `ExtractionCandidateView` and accept an optional `UnitInferred` on `ExpenseRequest`, mapped in `ToCommand`. Verify that 4.0 passes. Then run `./test-be.sh` and confirm the build is clean under `TreatWarningsAsErrors` (use the `be-editorconfig-fix` skill if not).

## 5. FE: inferred units on the review screen

- [x] 5.0 Write failing tests in `FE/src/capture/review.test.ts`: `lineOf` carries `unitInferred` from the candidate, `emptyLine` starts it false, `confirmationOf` sends it, and changing the unit clears it while changing the quantity keeps it and the unit unchanged ("Editing the quantity does not re-infer"). Run them and see them fail.
- [x] 5.1 Add `unitInferred` to the candidate type in `FE/src/api/types.ts`, the confirm line in `FE/src/api/writes.ts` and `EditableLine`. Clear it on a unit change (design D4). Verify that 5.0 passes.
- [x] 5.2 Write a failing component test for "An inferred unit is shown as a guess" (a hint beside an inferred line's unit, none beside a matched line's) and "Choosing a unit clears the guess". Run it and see it fail.
- [x] 5.3 Render the "guessed from quantity" hint in `FE/src/routes/CaptureReview.tsx` while `unitInferred` is set. Verify that 5.2 passes.

## 6. FE: pieces need a whole quantity

- [x] 6.0 Write failing tests in `FE/src/capture/review.test.ts` for the browser-client scenarios: "Pieces with a fractional quantity are refused", "Pieces with a whole quantity are accepted", "A fiscal line printed in pieces with a fractional quantity must be corrected", and "Other units allow fractional quantities". Run them and see them fail.
- [x] 6.1 Add the PCS whole-quantity check to the line validation in `review.ts`, reported on `unitCode` through the existing per-input marking (design D5). Verify that 6.0 passes, then run `./test-fe.sh` and confirm lint, format and type checks are clean (use the `fe-style-fix` skill if not).

## 7. Verification and docs

- [x] 7.1 Update `BE/tests/SCENARIO-COVERAGE.md` with the new and renamed scenarios. This is documentation and needs no test. Verify that every scenario in this change's deltas is listed with its test.
- [x] 7.2 End to end: capture a receipt whose line prints an unknown unit, confirm it untouched, and check that the expense has the inferred unit and that `GET /units` lists no new alias. Then repeat, choosing a unit by hand, and check that the alias is created. Run `./test-e2e.sh` if an e2e scenario covers capture review.

## Workflow follow-up

- Archive with `/opsx:archive default-unit-from-quantity` after implementation, which syncs the three delta specs into `openspec/specs/`.
