## Context

See proposal.md — Why. What shapes the approach:

- **The review screen holds the user's edits as strings.** `EditableLine` in `FE/src/capture/review.ts` keeps every numeric field as the text the user typed, deliberately: a half-typed `3.` is not a number. Validation therefore works on those strings, not on parsed values, and has to decide for itself what counts as a number.
- **`review.ts` already holds the screen's thinking.** `reviewReasons`, `lowConfidence` and `confirmationOf` are pure functions the component calls; the component only renders. The new rules follow the same split.
- **`asNumber` deliberately does not judge.** Its comment says rejecting a bad value there "would be a second place that decides what a purchase may contain". This change knowingly creates that second place — the point of the feature is that the client judges before the ledger does — so the two must be kept honest about each other rather than pretending one of them is authoritative.
- **A unit reaches an expense by two routes.** `ExpenseCommand.UnitCode`, resolved in `ExpenseAssembly.ResolveUnit`, and `ExpenseCommand.MatchedUnitId`, set by `ReceiptService.AsCommand` from a candidate matched during extraction. Only after both are consulted is it known whether a line has a unit.
- **`Expense.UnitId` must stay nullable.** Rows recorded before this change have no unit and must keep loading.
- **The capture response already names the engine.** `ExtractionResultView.engineName`; `result` is null only where extraction failed.

## Goals / Non-Goals

**Goals:**

- One statement of each rule per side of the wire, not one per input.
- Errors that point at an input, survive as the user types, and are legible to assistive technology.
- A server-side rule that is enforced where both routes to a unit are known, so its message can name the line.

**Non-Goals:**

- A validation library or a form framework. Five rules over a known shape do not pay for a dependency.
- Sharing the rules between FE and BE by generating one from the other. See D2.
- Blocking keystrokes or coercing input. The user types what they type; the screen judges it.
- Migrating or backfilling existing unit-less expenses.

## Decisions

**D1 — The rules are pure functions in `capture/review.ts`, returning errors keyed by field.**
A single `validate(edits): Errors` returns `{ amount?, occurredAt?, lines?: string, byLine: Map<key, Partial<Record<field, string>>> }` — every problem found, not the first. The component renders marks from that map and asks one question per input. Rejected: validating inside the component, which would mix the rules into JSX and make them testable only by rendering; and per-field validator props, which would scatter the same rules across six inputs.

**D2 — The 200-character limit and the unit rule are stated on both sides, and the FE states why.**
The FE duplicates limits the ledger enforces. The alternative — letting the server be the only judge — is what this change exists to remove, and generating one side from the other is a build step and a schema for two constants. The duplication is accepted with a comment on the FE constant naming the domain rule it mirrors, so a future change to the limit has a thread to follow. If the two ever disagree, the ledger wins: its rejection still reaches the screen through the existing rejection path.

**D3 — Errors appear on the first confirm attempt, then track the values live.**
State holds a single `attempted` flag, not per-field touched state. Before the first confirm the screen marks nothing; after it, the error map is recomputed from the current values on every render, so a corrected field clears itself and a newly emptied one marks itself. This satisfies "a correction clears the mark" without a second mechanism, and avoids marking fields the user has not reached yet. Rejected: validating from the first keystroke (marks a line the user is still typing) and freezing the errors from the submit attempt (a corrected field would stay marked until the next attempt).

**D4 — A marked input carries `aria-invalid`, a message linked by `aria-describedby`, and a non-colour mark.**
The message element sits next to the input with a stable id derived from the line key and field name. The mark is a border weight plus the message text, not colour alone. The existing `data-testid` convention on the review screen is followed for the new elements so the e2e test can address them.

**D5 — The unit rule is enforced in `ExpenseAssembly`, the description limit in `Expense`.**
`ExpenseAssembly.Build` is the one place where a resolved `UnitCode` and a `MatchedUnitId` are both in hand, and it iterates lines with an index, so it can raise `expense.unit_required` naming the line. The description limit is an invariant of the entity — it sits beside the existing non-empty check in `Expense.Record` as `expense.description_too_long`, translated by `DomainErrorTranslation.Expense` like its neighbours. Putting the unit rule in the entity instead was rejected: `UnitId` must stay nullable for existing rows, so the entity cannot state "always set" without lying about what it can represent.

**D6 — `ReceiptService.AsCommand` stops passing `UnitCode: null` silently, and a unit-less candidate fails loudly.**
Confirming a capture with no edited lines builds commands straight from candidates. Where a candidate matched no unit, that confirmation now fails with `expense.unit_required` naming the line. Rejected: defaulting to a count unit such as `PCS`, which invents a fact about the receipt — the same reason the codebase keeps `unitRaw` beside a matched unit. The browser client is unaffected because its form requires a unit before submitting; an MCP caller must pass the lines with units.

**D7 — The engine name is shown from `capture.result.engineName` alone.**
No fallback, no inference from `stepsRun`. Where `result` is null the screen says no engine reading is available. The name sits in the screen's header, above the fields, since it qualifies everything below it.

**D8 — The wire shapes keep `UnitCode` optional, and say in words that it is required.**
Making it a required constructor parameter on `ExpenseRequest` was tried and reverted: System.Text.Json then refuses to bind a payload without it, and the client gets `request_invalid` — "the request could not be read" — instead of `expense.unit_required` naming the line, which is the whole point of the rule's error shape. The same reasoning is already written at the top of `ExpenseRequest.cs`: no rule may live in an adapter. The shapes document the requirement; the use case enforces it.

## Risks / Trade-offs

- **[The FE and BE rules drift apart.]** → The limit constant on each side names the other in a comment, and the spec states the rule once for both. A drifted FE is not a correctness failure: the ledger still rejects, and the rejection still reaches the screen.
- **[The MCP `confirm_capture` shortcut becomes unusable for receipts where no unit was matched.]** → Deliberate (D6). The failure names the line and the caller resubmits with `expenses` supplied. Worth watching: if most candidates match no unit, the rule makes the shortcut useless in practice and the matching, not the rule, is what needs work.
- **[The Avalonia client starts getting rejections it does not pre-empt.]** → Out of scope and flagged in the proposal. Its existing rejection path shows the ledger's message, so it degrades to today's behaviour rather than breaking.
- **[An error message read as part of a field's name.]** Found by the e2e run, not by the unit tests: the message element inside a wrapping `<label>` became part of the input's accessible name ("Amount An amount is required."). The label now wraps only the caption and the control, with the message a sibling linked by `aria-describedby`, and a test pins the accessible name so it cannot come back.
- **[Marking inputs on a long line list pushes content around.]** → The message element is rendered in reserved space within the field, so marking a line does not reflow the ones below it.
- **[Requiring a unit on every line slows down manual entry after a failed extraction.]** → Accepted: it is the rule the user asked for. The unit select keeps its current ordering; no default is pre-selected, because a pre-selected unit would satisfy the rule without the user having read it.

## Migration Plan

No data migration. The rules apply to newly recorded expenses; stored rows with no unit or a long description are untouched and keep loading. Deploy order does not matter: a stricter API paired with the old client produces the rejection the old client already renders, and the new client with the old API sends units the API accepts as it always has.

Rollback is reverting the change; nothing recorded under it becomes invalid afterwards.
