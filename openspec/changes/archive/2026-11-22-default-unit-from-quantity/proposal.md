# Proposal

## Why

A receipt line whose printed unit matches no code or alias, or that prints no unit at all, reaches review with no unit, and every such line has to be given one by hand before the purchase can be confirmed. Most of these lines are either counted items or weighed ones, and the quantity already says which: a whole number is almost always pieces, a fraction almost always kilograms. This change reverses the earlier decision to refuse rather than default (the `AsCommand` rationale in `ReceiptService`). The person confirming still reviews the guess, and a guess must never become a permanent alias.

## What Changes

- After extraction, a candidate line that matches no unit (unknown printed text, or none printed) gets a unit **inferred from its quantity**: PCS when the quantity (1 when absent) is a whole number, KG otherwise. The line is marked as having an inferred unit. Where the unit to infer is inactive, the line stays without a unit as today.
- The inferred mark is carried through the candidate view, the review screen and the confirmation request. A line confirmed with its inferred unit untouched **teaches no unit alias**. Choosing a unit by hand clears the mark, and learning then applies as before.
- The review screen shows that a unit was guessed from the quantity, and does not guess again when the user edits the quantity.
- The review screen refuses a line with the unit PCS whose quantity is not a whole number. The rule is enforced in the client only; the ledger's own rules for recorded expenses do not change.
- Direct recording (API and MCP) is unchanged: no unit is inferred there, and a line without a unit is still rejected.
- **BREAKING (behaviour):** confirming a capture's candidates unedited now succeeds for lines whose unit was previously unmatched, rather than being refused for a missing unit.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `receipt-ingestion`: "Extracted unit text is matched to a unit". An unmatched line, including one with no printed unit, carries a unit inferred from its quantity and is marked as inferred, instead of carrying no unit.
- `purchase-recording`: "Saving an expense line teaches a unit alias". A line saved with its inferred unit untouched teaches no alias.
- `browser-client`: "The review screen refuses an incomplete purchase". A PCS line with a fractional quantity is refused. "A successful capture is presented for review": an inferred unit is shown as a guess, and changing the unit clears the inferred mark.

## Impact

- **Domain**: `ExtractionCandidate` gains an inferred-unit flag and a way to set an inferred unit beside `MatchUnit`.
- **Application**: `ReceiptService.MatchUnits` widens to lines without printed text and applies the quantity rule on a miss. `ExpenseCommand` carries the flag, `AsCommand` passes it through, and `ExpenseAssembly` skips alias learning for inferred lines.
- **API**: `ExtractionCandidateView` exposes `unitInferred`, and `ExpenseRequest` accepts an optional `unitInferred` (default false). The change is additive: existing clients keep their current behaviour.
- **FE**: the `EditableLine` / `lineOf` / `confirmationOf` flow in `capture/review.ts`, the unit validation, and the hint in `CaptureReview.tsx`.
- **Data**: candidates are transient, so no migration is needed. Already-recorded expenses are not touched.
