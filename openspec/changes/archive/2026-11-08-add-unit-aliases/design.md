## Context

See proposal.md for why. The state this design works from:

- Both extraction stages already report the printed unit verbatim — `FiscalPortalClient.Map` and `ClaudeVisionReceiptExtractor` pass `unitRaw` to `ExtractionCandidate.Propose` — and neither passes `unitId`. `ExtractionCandidate` exposes only private setters, so nothing after a stage can record a match either.
- Every extraction run goes through `ExtractionCascade.Run`, and all three callers live in `ReceiptService`: `Capture`, `CaptureFiscal` and `RerunExtraction`. A capture's result is returned to the caller and not held (candidates are held by the caller); a re-run's result is stored by `candidates.Replace` and read back later by `GetExtractionCandidates` and `ConfirmCandidates`.
- Every saved expense line — `record_purchase`, confirming candidates, confirming a capture — passes through `ExpenseAssembly.Build`, which resolves `UnitCode`, falls back to `MatchedUnitId`, and refuses a line with neither. Everything it builds is saved in the caller's one `SaveChanges` (D16).
- Units are seeded by `HasData` with fixed identifiers 1–7 (D15). Categories use `UseAsyncSeeding` with an upsert by code.
- The frontend's review screen already preselects a line's unit from `candidate.unitId` (`codeOfUnit`), so a matched candidate needs no client change to show up matched. The `units` query is held for an hour.

## Goals / Non-Goals

**Goals:**

- One definition of "this printed text means this unit", used identically by extraction, by saving a line, and by alias management.
- Matching needs no change in any extraction stage, and covers future stages without them knowing about it.
- Learning is exactly as transactional as the expense it comes from.

**Non-Goals:**

- Re-matching expenses already recorded.
- Per-merchant aliases. An alias is global.
- Fuzzy matching, and extracting quantity-with-unit from text such as "100g" or "1,45L". Inner characters are significant, so those stay unmatched.
- Changing the unit dictionary, or giving units a create, rename or deactivate use case.
- Deriving a unit from the line description ("JAJA /kom/").

## Decisions

### D1 — Aliases are their own table, not a column on `units`

```
unit_aliases
  id        bigint GENERATED ALWAYS AS IDENTITY  PK   -- as categories; the seed supplies no ids
  unit_id   bigint NOT NULL  FK -> units(id)  ON DELETE RESTRICT
  alias     text   NOT NULL  UNIQUE          -- normalised form only
  INDEX (unit_id)
```

A new `UnitAlias` domain entity (`Id`, `UnitId`, `Alias`) with a factory that refuses an empty alias. `Unit` is not given an alias collection.

**Why:** uniqueness across all units is a rule between rows, which a unique index states directly; a `text[]` column on `units` cannot express it. `units` rows are owned by `HasData`, so writing to them at runtime would put the model and the database at odds. Keeping `Unit` free of an alias navigation keeps it the fixed thing D15 describes; the global uniqueness rule cannot live inside one `Unit` aggregate anyway, so it lives in the application service and the index.

**Alternative:** an `Aliases` collection owned by `Unit`. Rejected, because the rule that matters crosses aggregates, and loading every unit to add one alias buys nothing.

### D2 — The starting aliases are inserted by the migration, once

The migration that creates `unit_aliases` inserts the starting set with `migrationBuilder.InsertData`, referencing the seeded unit identifiers (PCS = 1, PACK = 2, KG = 4, G = 5, L = 6).

**Why:** the spec requires a removed starting alias to stay removed.

- `UseAsyncSeeding` runs on every start, so it would restore a deleted alias.
- `HasData` makes the model own the rows. A user's deletion leaves the model believing a row exists, and a later migration that touches seed data could re-insert it or fail on it.
- A migration runs exactly once per database, which is the property wanted.

**Trade-off:** changing the starting set later means a new migration, not a code edit. That is acceptable, because users can add aliases themselves.

### D3 — One normaliser, returning a plain string

A static `UnitText.Normalise(string? text) → string?` in the application layer, consistent with the preference for primitives over value objects:

1. null → null
2. trim surrounding whitespace, and collapse internal whitespace runs to one space
3. trim `.` and `/` from both ends, repeating steps 2–3 until stable (`" /kom/. "` → `kom`)
4. lowercase, culture-invariant
5. empty → null

It is used for every comparison: storing and looking up aliases, matching printed text, and comparing text against unit codes (`code.ToLowerInvariant()`). The database never normalises. The `alias` column only ever receives normalised values, so a plain unique index is enough, with no functional index or `citext`.

### D4 — A unit lookup built once per operation

An application-layer `UnitLookup`, loaded from the unit and alias repositories, maps normalised text to a unit ID. It answers two different questions:

- `Match(raw) → long?`: active units only. It tries codes first, then aliases.
- `IsKnown(raw) → bool`: all units, active or not. It is true if the text is any unit's code or any existing alias. This backs learning's rule that "known" text is never learned.

The dictionary is tiny (7 units, a few dozen aliases), so loading it whole per extraction or per save is cheaper and simpler than one query per line.

### D5 — Matching runs in `ReceiptService`, after the cascade and before anything reads the result

A private `MatchUnits(ExtractionStepResult)` is called immediately after each of the three `cascade.Run` calls, before `Judge`, before `candidates.Replace`, and before any view is built. For each candidate with `UnitRaw`, it calls the new `ExtractionCandidate.MatchUnit(long unitId)`, which mirrors `Expense.MatchUnit` and leaves `UnitRaw` untouched (D9).

**Why here:**
- It's one place for every path.
- It needs no change to any stage.
- The stored re-run result is already matched, so `GetExtractionCandidates` and `ConfirmCandidates` need nothing extra.

**Alternatives considered:**
- **Inside each extractor:** duplicated, puts reference-data access in infrastructure clients, and a future stage could forget it.
- **Inside `ExtractionCascade`:** the cascade orders stages and deliberately stays free of reference data and judgement (D28).
- **At read time:** a capture's result is never read back, so it would be missed.

A stage that someday sets `unitId` itself keeps it: `MatchUnits` only fills a candidate whose `UnitId` is null.

### D6 — `ExpenseAssembly` resolves from printed text, then learns

`Build` takes the `UnitLookup` and an alias repository. For each line:

```
unitId = ResolveUnit(UnitCode)            // explicit code wins
      ?? MatchedUnitId                    // extraction's match
      ?? lookup.Match(UnitRaw)            // printed text, for callers that sent only that
      ?? refuse (expense.unit_required)

if normalised(UnitRaw) is not null
   and not lookup.IsKnown(UnitRaw)
   → aliases.Add(UnitAlias.Create(unitId, normalised))
     lookup.Remember(normalised, unitId)  // the earlier line in this save decides
```

The alias is added to the change tracker, not saved on its own. It is written by the same `SaveChanges` as the purchase, or discarded with it when reconciliation or anything else refuses the save. This follows D16 and needs no compensation.

Learning never overwrites. Text that is already known, even through an inactive unit's alias, is left alone. That rule is the whole of the conflict policy the spec asks for.

### D7 — Alias management lives in `UnitService`

`UnitService` gains:

- `List`, now returning `UnitView` with `Aliases` (sorted, ordinal).
- `AddAlias(code, alias)` → `UnitView`.
- `RemoveAlias(code, alias)`.

Order of checks for `AddAlias`:

1. Normalise; empty → `unit_alias.empty`.
2. Unit by code; missing → `unit.not_found`; inactive → `unit.inactive`.
3. Equals any unit's code → `unit_alias.is_unit_code`.
4. Existing alias: same unit → return unchanged (idempotent); other unit → `unit_alias.conflict`, naming the alias and the unit's code.
5. Add and save.

`RemoveAlias` normalises the alias, then returns `unit_alias.not_found` when the alias is missing or belongs to another unit. `unit.creation_not_supported` stays as it is.

HTTP status mapping follows `ErrorHandling`: `*.not_found` → 404. `unit_alias.conflict` and `unit_alias.is_unit_code` are added to the 409 list. `unit_alias.empty` → 400.

### D8 — HTTP and MCP shapes

- `GET /units`: unchanged route. Each item gains `aliases: string[]`.
- `POST /units/{code}/aliases` with body `{ "alias": "kut" }` → 200 with the `UnitView`, both when the alias is added and when it already existed. The client needs no distinction, and returning 201 on first add and 200 on repeat would imply a resource ID the design doesn't give aliases.
- `DELETE /units/{code}/aliases?alias=kut` → 204. The alias travels as a query parameter, not a path segment, because a normalised alias may contain an inner `/` or `.`, which routing does not round-trip reliably.
- MCP: `list_units` returns the same `UnitView`. New `add_unit_alias(unit_code, alias)` returns the `UnitView`. New `remove_unit_alias(unit_code, alias)` reports success. Both call `UnitService`, with no rules in the tool.

### D9 — Client: a `/units` route and cache invalidation

- A `Units` route lists active units, showing name, symbol and alias chips. Each unit has an add field and a remove control per chip. A link from Home leads to it.
- Mutations use `@tanstack/react-query` `useMutation`. On success they invalidate `queryKeys.units`.
- The existing capture-confirm mutation also invalidates `queryKeys.units`, because a confirm may have taught an alias. Without that, the hour-long stale time would hide the learned alias the spec says the screen shows.
- `UnitView` in `api/types.ts` gains `aliases: string[]`. The review screen is unchanged.

## Risks / Trade-offs

- **[Two saves learn the same new alias at the same moment]** → The second `SaveChanges` hits the unique index and its whole purchase is refused. This is a single-user ledger, so it's accepted. Retrying the save succeeds, because the alias is then known and nothing is learned.
- **[A mistaken pick teaches a wrong alias]** → Every later line with that spelling is matched wrongly until someone notices. The mitigations are that a matched unit is shown preselected at review, and that the units screen and `remove_unit_alias` undo it in one step. Learning never overwrites, so one mistake can't cascade into re-pointing a correct alias.
- **[A deliberate one-off override looks like a teaching moment]** → It isn't one: an override of known text teaches nothing (D6).
- **[Short aliases collide across languages]** → For example, "st" is a piece in German and a tile or stone elsewhere. Global uniqueness means the first mapping wins. It's accepted, and the alias can be removed.
- **[Loading the lookup per operation]** → It's negligible at this size. If the alias table ever grew to thousands, D4's lookup is the single place to add caching.
- **[Vision reads a unit wrongly]** → A misread unit could match the wrong unit. This is unchanged by this design: the unit's reported confidence still marks the line low-confidence (D20), and the review screen still shows it.

## Migration Plan

1. One EF migration: create `unit_aliases` with its unique and foreign-key indexes, then `InsertData` the starting set (D2). No data in existing tables changes.
2. Deploy as usual: migration bundle outside development, or migrate on start where `Database:ApplyMigrationsOnStartup` is set.
3. **Rollback:** the migration's `Down` drops `unit_aliases`. Expenses matched through an alias keep their `unit_id` and `unit_raw`, which are ordinary columns, so rolling back loses only the aliases themselves.
