## Why

Every receipt line arrives with its unit unmatched, so no receipt can be confirmed without picking a unit by hand on every line. The fiscal portal and the vision engine both read the unit correctly — receipt `1000023219.jpg` comes back as `KOM` ×8 and `KG` ×6 from both — but nothing turns that printed text into a unit reference: both stages fill `UnitRaw` and leave `UnitId` empty, and no later step resolves it. D9 of the original ledger design promised exactly this match ("the foreign keys only when a confident match exists"); it was never built. Printed spellings are local and open-ended (`KOM`, `kd`, `Stk`, `kgr`, `ltr`), so the mapping from what a receipt prints to the fixed unit dictionary has to be data that grows, not a list compiled into the code.

## What Changes

- A new **unit alias** dictionary maps a normalised printed spelling to exactly one unit. An alias is unique across all units and may not equal any unit's code, since codes already match on their own. Units themselves stay fixed reference data.
- A starting set of aliases is seeded once (`kom`, `kd`, `kos`, `stk`, `st`, `pc` → PCS; `kgr` → KG; `gr` → G; `lit`, `ltr` → L; `pak`, `pkg` → PACK). An alias the user removes is not restored.
- **Extraction matches units.** After any extraction — image capture, fiscal-only capture, or a re-run on a stored receipt — each candidate's printed unit text is normalised and matched first against unit codes, then against aliases. A match fills the candidate's unit; no match leaves it empty. The printed text is kept either way. Inactive units, and aliases of them, are not used for matching.
- **Saving a line resolves its printed unit.** A saved expense line that supplies printed unit text but neither a unit code nor an extraction match is resolved through the same code-then-alias match, so a caller that passes only what the receipt printed is not refused.
- **Aliases are learned when a line is saved.** When a saved expense line carries printed unit text whose normalised form matches no code and no alias, and a unit was chosen for the line, that pairing is recorded as a new alias in the same transaction. This applies to confirming candidates, confirming a capture, and recording a purchase manually. An existing alias is never overwritten by learning: a line saved with a different unit keeps its own unit, and the alias stands.
- **Aliases can be managed explicitly** over HTTP and MCP: listed with their unit, added to a unit, and removed. Removing an alias leaves expenses already matched through it unchanged.
- **Unit listings carry their aliases** — the HTTP unit listing and the MCP `list_units` tool.
- **The browser client gets a units screen** that lists each unit with its aliases and lets the user add and remove aliases.
- **No re-matching of history.** Expenses already recorded with printed unit text and no unit are left as they are.

## Capabilities

### New Capabilities

_None._ Aliases belong to the unit dictionary and are specified within `reference-data`.

### Modified Capabilities

- `reference-data`: adds the unit alias dictionary (normalised, globally unique, seeded once, learned, managed), states that units stay fixed while their aliases are editable, and has unit listings carry aliases.
- `receipt-ingestion`: extracted unit text is matched to a unit through codes and aliases, with this receipt's `KOM`/`KG` as the worked example.
- `purchase-recording`: a line's unit may also be established by its printed text matching a code or alias; saving a line with printed unit text and a chosen unit teaches an alias when the text is unknown, without overriding an existing alias.
- `api-surface`: HTTP endpoints and MCP tools to list, add and remove unit aliases; the unit listing carries aliases.
- `browser-client`: a units screen for viewing and managing aliases.

## Impact

- **Database**: new `unit_aliases` table (`unit_id` → `units`, `alias` unique), one migration that also inserts the starting aliases once — neither `HasData` nor startup seeding, either of which would restore an alias the user removed.
- **Domain**: new `UnitAlias` entity; `ExtractionCandidate` gains a way to record a matched unit.
- **Application**: one alias normaliser shared by matching, learning and management; a unit matcher applied in `ReceiptService` after each extraction run; unit resolution from printed text and alias learning in `ExpenseAssembly`; `UnitService` gains alias listing and management; `UnitView` carries aliases; new error codes for an unknown alias, a duplicate alias, and an alias that normalises to nothing.
- **Infrastructure**: alias repository and EF configuration; seed extended.
- **API**: `GET /units` carries aliases; `POST /units/{code}/aliases`, `DELETE /units/{code}/aliases/{alias}`.
- **MCP**: `list_units` carries aliases; new `add_unit_alias`, `remove_unit_alias` tools.
- **FE**: `UnitView` type gains aliases; new `/units` route; the review screen shows matched units without change, since it already preselects `unitId`.
- **Not affected**: extractors themselves (they keep reporting printed text only), stored expenses (no re-matching), the rule that a confirmed line needs a unit.
