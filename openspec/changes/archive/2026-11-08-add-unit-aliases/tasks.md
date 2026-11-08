## 1. Normalisation and domain

- [x] 1.0 Write failing tests. `Expenses.Application.Tests/ReferenceData/UnitTextTests`: case, surrounding and internal whitespace, end periods and slashes, inner characters kept, empty result → null, from "Spellings that differ only in form are one alias", "Inner characters are significant" and "Text that normalises to nothing". `Expenses.Domain.Tests/UnitAliasTests`: `UnitAlias.Create` refuses an empty alias. `ExtractionCandidateTests` (or the nearest existing class): `MatchUnit` sets `UnitId` and leaves `UnitRaw` unchanged, from "Unit text is preserved alongside a match". Run them and see each fail.
- [x] 1.1 Add `UnitText.Normalise(string?) → string?` in the application layer (design D3).
- [x] 1.2 Add the `UnitAlias` entity (`Id`, `UnitId`, `Alias`) with a `Create` factory (design D1).
- [x] 1.3 Add `ExtractionCandidate.MatchUnit(long unitId)`, mirroring `Expense.MatchUnit` (design D5).

## 2. Persistence (exempt from test-first: mapping and generated migration; behaviour is covered in 3.0)

- [x] 2.1 Add `UnitAliasConfiguration`: table `unit_aliases`, `id` as an always-generated identity, `unit_id` FK to `units` with restrict, unique index on `alias`, index on `unit_id`; add `DbSet<UnitAlias>`.
- [x] 2.2 Add the `IUnitAliasRepository` port (list all, find by alias, add, remove) and its EF implementation.
- [x] 2.3 Generate the migration and add `InsertData` for the starting set in `Up` (design D2): kom, kd, kos, stk, st, pc → 1; kgr → 4; gr → 5; lit, ltr → 6; pak, pkg → 2. Check `Down` drops the table.

## 3. Persistence behaviour

- [x] 3.0 Write failing integration tests in `Expenses.Integration.Tests/Persistence`: "Starting aliases are present"; "A removed starting alias stays removed" (remove, rebuild the context, and run the startup seeding path again); "A user alias survives restart"; the unique index refuses a second row with the same alias for another unit ("One alias, one unit", storage side). Run them and see them fail.
- [x] 3.1 Make 3.0 pass. Fix mapping or migration only, with no application logic.

## 4. Unit lookup and extraction matching

- [x] 4.0 Write failing tests in `Expenses.Application.Tests/Receipts` over fakes (extend `InMemoryLedger` with aliases): "A fiscal invoice's units are matched" (14 lines, KOM → PCS via alias, KG → KG via code, raw text unchanged); "Vision-extracted units are matched"; "A code match is preferred to an alias"; "Unknown text leaves the line unmatched"; "Unit codes match in normalised form"; "An alias of an inactive unit does not match"; "A re-run uses the aliases as they now stand"; a stage-supplied `unitId` is kept. Cover `Capture`, `CaptureFiscal` and `RerunExtraction`. Run them and see them fail.
- [x] 4.1 Add `UnitLookup` (design D4): `Match` over active units, codes then aliases; `IsKnown` over all units and aliases; `Remember`.
- [x] 4.2 In `ReceiptService`, add `MatchUnits` and call it immediately after each of the three `cascade.Run` calls, before `Judge`, `candidates.Replace` and view building (design D5). Inject `IUnitAliasRepository`.

## 5. Saving lines: resolve from printed text, and learn

- [x] 5.0 Write failing tests in `Expenses.Application.Tests/Purchases` and `Receipts`: "Printed unit text alone satisfies the rule"; "A supplied unit code overrides the printed text"; "An expense without a unit is rejected" (updated wording: unknown raw text and no code); "An unknown spelling is learned" (confirm capture); "Learning applies to direct recording"; "A known alias is not overridden"; "A matched unit teaches nothing"; "A code teaches nothing"; "The earlier line decides within one save"; "A rejected save teaches nothing"; "History is not re-matched"; "A receipt whose units all match can be confirmed unchanged". Run them and see them fail.
- [x] 5.1 Change `ExpenseAssembly.Build` to take the `UnitLookup` and the alias repository. Resolve code → matched → printed text; learn an unknown normalised text into the tracker and `Remember` it (design D6). Update its three callers.
- [x] 5.2 Update the XML doc on `ReceiptService.AsCommand`, which says no unit is ever inferred, to reflect D6.

## 6. Alias management service

- [x] 6.0 Write failing tests in `Expenses.Application.Tests/ReferenceData`: "Adding an alias"; "Adding an alias the unit already has"; "Adding an alias to an unknown unit"; "Adding an alias to a deactivated unit"; "An alias may not repeat a unit code"; "One alias, one unit" (error names PCS); "The alias is stored normalised"; "Text that normalises to nothing"; "Removing an alias"; "Removing an alias from the wrong unit"; "Removing an alias leaves history unchanged"; "Units are listed with their aliases"; "Adding an alias does not change the unit". Assert on error codes. Run them and see them fail.
- [x] 6.1 Add the error codes `unit_alias.empty`, `unit_alias.is_unit_code`, `unit_alias.conflict` and `unit_alias.not_found` to `ApplicationErrors`.
- [x] 6.2 Add `Aliases` to `UnitView`; have `UnitService.List` join the aliases in, sorted ordinally.
- [x] 6.3 Add `UnitService.AddAlias` and `RemoveAlias` with the check order in design D7. Update the class XML doc: units stay fixed, aliases do not.

## 7. HTTP and MCP

- [x] 7.0 Write failing tests. `Expenses.Integration.Tests/Api/HttpAdapterTests`: "Listing units over HTTP carries aliases"; "Adding an alias over HTTP" (200 with the view); "Removing an alias over HTTP" (204, query parameter); "Aliases are addressed in normalised form"; "Units cannot be created through alias operations" (404); conflict → 409 in the standard error shape. `Mcp/McpAdapterTests`: "Listing units over MCP carries aliases"; "Adding an alias over MCP"; "Removing an alias over MCP"; "Repeating an add is safe". `Ledger/CrossAdapterTests`: "A conflicting alias is rejected the same way on both interfaces". Extend `Extraction/FiscalJourneyTests` with the `verify-32AA324CFF5030271E16D59F7F8EF636.json` fixture for "A fiscal invoice's units are matched" end to end. Run them and see them fail.
- [x] 7.1 Map `unit_alias.conflict` and `unit_alias.is_unit_code` to 409 in `ErrorHandling`; check `unit_alias.empty` maps to 400 and `unit_alias.not_found` to 404.
- [x] 7.2 Add `POST /units/{code}/aliases` and `DELETE /units/{code}/aliases?alias=` to `UnitsController` (design D8).
- [x] 7.3 Add the MCP tools `add_unit_alias` and `remove_unit_alias` in `ReferenceDataTools`, with argument schemas and descriptions that say when to use them; check `list_units` now returns aliases.

## 8. Browser client

- [x] 8.0 Write failing Vitest and Testing Library tests: `api/writes.test.ts` for the add and remove calls (URL, method, body, query parameter); `routes/Units.test.tsx` for "The units screen lists aliases", "Adding an alias", "Removing an alias", "A conflicting alias is reported in place", and "Aliases can be read when they cannot be changed"; `routes/Home.test.tsx` for "The units screen is reachable from home"; a test that a successful capture confirm invalidates the units query, for "A learned alias appears". Run them and see them fail.
- [x] 8.1 Add `aliases: string[]` to `UnitView` in `api/types.ts`; add `addUnitAlias` and `removeUnitAlias` to `api/writes.ts`.
- [x] 8.2 Add the `Units` route and screen with its CSS module, register `/units` in `routes.tsx`, and link it from Home (design D9).
- [x] 8.3 Invalidate `queryKeys.units` after alias mutations and after a successful capture confirm.
- [x] 8.4 Run the `fe-style-fix` workflow (format, lint:fix, structure check) until clean.

## 9. Wrap-up (exempt from test-first: documentation and verification)

- [x] 9.1 Run the `be-editorconfig-fix` workflow until the build is clean under `TreatWarningsAsErrors`.
- [x] 9.2 Run all three BE test projects and the FE suite; record the pass counts.
- [x] 9.3 Update `BE/db/schema.md` with `unit_aliases`, and `BE/tests/SCENARIO-COVERAGE.md` with the new scenarios and where they are tested.
- [x] 9.4 Update the XML docs on `Unit`, `UnitSeed` and `IUnitRepository` that describe units as having no editable parts, so they point to aliases.
