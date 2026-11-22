# Design

## Context

Units are resolved in one place after extraction: `ReceiptService.MatchUnits` runs on every path (image capture, fiscal QR only, re-run on a stored receipt) before the result is judged, stored or shown. Today it only looks at candidates with printed text, and leaves a miss with `UnitId = null`. At save time `ExpenseAssembly.Build` resolves a unit as caller code, then the candidate's match, then a fresh alias lookup, and rejects a line that resolves none. After saving, it learns the normalised printed text as an alias of the saved unit.

On the review screen, `lineOf` (FE `capture/review.ts`) pre-fills `unitCode` from the candidate's `unitId`, and `confirmationOf` always sends that code back. So on the FE path the server can't tell a unit the user accepted from one the user chose. That is why the inferred state has to travel with the line instead of being reconstructed on the server.

See proposal.md for motivation.

## Goals / Non-Goals

**Goals:**
- Inference happens once, at the candidate stage, so the guess is visible on the review screen.
- A guess never becomes an alias unless a person replaces it with a chosen unit.
- The PCS-needs-a-whole-quantity check is a review-screen rule.

**Non-Goals:**
- No inference on direct recording (API or MCP). A line without a unit is still rejected there.
- No ledger-side rule tying PCS to a whole quantity. Direct recording and already-recorded expenses can still carry PCS with a fraction.
- No change to the extraction state (Extracted/NeedsReview). An inferred unit doesn't mark a capture for review.
- No inference across other count units (PACK, BUNCH) or volume units. The rule only ever picks PCS or KG.

## Decisions

**D1. Infer inside `MatchUnits`, after the lookup misses.** The filter widens from "no unit and has printed text" to "no unit". On a miss, take `quantity ?? 1`. If it is a whole number (`q == decimal.Truncate(q)`, which treats `1.000` as whole), infer PCS, otherwise KG, both looked up by code among active units. A candidate the stage already matched is left alone, as today.
*Alternative:* infer in `ExpenseAssembly` at save time. Rejected: the user wouldn't see the guess before confirming, and it would reach direct recording unless gated.

**D2. `ExtractionCandidate.InferUnit(long unitId)` sets `UnitId` and `UnitInferred = true`. `MatchUnit` leaves `UnitInferred` false.** Keeping it a separate method keeps "matched" and "inferred" distinct in the domain. The flag is stored with the candidate wherever candidates are held, so a stored re-run or a later confirm-from-candidates still sees it. A re-run creates new candidates, so aliases added since are tried before inference again.

**D3. The flag reaches the server as an assertion from the caller, `unitInferred` on `ExpenseRequest`/`ExpenseCommand` (default false).** `AsCommand` passes the candidate's flag on the unedited path, and the FE sends it while the guess is untouched. `ExpenseAssembly` skips learning when it is set, and does nothing else with it.
*Alternatives:*
- Omit `unitCode` for untouched lines and let the server re-infer: rejected, because it needs inference at save time (excluded by D1) and can't tell an untouched guess from a cleared unit.
- Compare against the stored candidate on the server: rejected, because captures hold no candidates server-side once the capture response is returned (the client carries them).
*Why it's safe to trust:* the flag can only turn alias learning off for a line. It never supplies a unit or makes an invalid line valid.

**D4. FE: `EditableLine.unitInferred`.** Set from the candidate in `lineOf` and cleared in the unit `onChange`. A quantity edit leaves it alone, so the guess is static. `confirmationOf` sends it. `CaptureReview.tsx` shows a "guessed from quantity" hint beside the unit select while it's set. Hand-added lines (`emptyLine`) start with it false.

**D5. The PCS whole-quantity check is in the FE validator only.** `validate` in `review.ts` adds a `unitCode` error when the code is `PCS` and the parsed quantity isn't a whole number. The error uses the existing per-input marking. The unit is identified by its code `PCS`, a stable identity (D8 of the unit dictionary), so no kind lookup is needed. The rule covers a fiscal `KOM` line with a fractional quantity, which the user has to re-unit before confirming. Matching itself doesn't change, so the invoice stays authoritative as a reading.
*Trade-off accepted:* the ledger still accepts PCS with a fraction from direct recording, and from server-side confirm-from-candidates without edited lines (a fiscal `KOM` 0.350 confirmed that way). This was chosen to keep the ledger's rules unchanged.

## Risks / Trade-offs

- [A weighed item at exactly 1.000 kg with no printed unit is guessed as PCS] → the hint shows on the review screen. Correcting it to KG teaches no alias when nothing was printed.
- [A volume line with a fractional quantity and unknown text (e.g. "LIT" 1.5) is guessed as KG, and the screen doesn't flag the mismatch] → the guess is shown as such, and replacing it with L teaches "lit" ⇒ L, so the next receipt matches.
- [A user confirms a wrong guess unedited] → it is stored with the wrong unit but teaches nothing, so the damage stays on that one line.
- [An older FE build doesn't send `unitInferred`] → it defaults to false, so a guess confirmed through that build is learned. This is acceptable because client and server are deployed together.
- [PCS or KG deactivated] → nothing is inferred for that case, and the line is left without a unit as today.
- [The rationale comment on `AsCommand` says the opposite of the new behaviour] → rewrite it in the same change.

## Migration Plan

No schema migration. Candidates are held by `InMemoryExtractionCandidateStore`, so the flag lives on the object, and expenses don't store it. Deploy BE and FE together. Rolling back the BE leaves `unitInferred` ignored by binding, and candidates go back to carrying no unit on a miss.
