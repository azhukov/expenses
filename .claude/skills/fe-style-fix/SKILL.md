---
name: fe-style-fix
description: Clear the FE style, type and structure diagnostics — Prettier layout, ESLint rules (type-aware ones included), tsc errors, and the src/ shape rules in structure.config.json — deterministically with npm run format and lint:fix first, and only then by hand. Use when the FE workflow fails, after tightening a rule, when adding a feature directory or a new component, or when asked to clean up style in the React/Vite client.
allowed-tools: Bash, Read, Edit, Write, Glob, Grep
---

# Fixing FE style, type and structure diagnostics

The FE gate is four tools, split by what each is good at, and
[.github/workflows/fe.yml](../../../.github/workflows/fe.yml) runs all four:

| Tool | Owns | Config |
|---|---|---|
| Prettier | layout — nothing a human should argue about | [FE/.prettierrc.json](../../../FE/.prettierrc.json) |
| ESLint | what a formatter cannot decide: correctness, type-aware rules, the import graph | [FE/eslint.config.js](../../../FE/eslint.config.js) |
| tsc | types, plus `noUnusedLocals` / `noUnusedParameters` | [FE/tsconfig.app.json](../../../FE/tsconfig.app.json) |
| `check-structure` | where a file lives and what it is called | [FE/structure.config.json](../../../FE/structure.config.json) |

`npm run style` is all four in one command. This skill clears them in one direction: **every
diagnostic a tool can fix is fixed by the tool; you only touch the ones no fixer exists for.**

Never silence a diagnostic to make it go away — no `eslint-disable`, no `@ts-ignore` or
`@ts-expect-error`, no adding a path to `.prettierignore`, and above all no editing
`structure.config.json` to make a violation legal. If a rule genuinely does not fit this codebase,
stop and say so; changing the rule is the user's call, not yours.

## 0. Baseline

```
git status --short          # must be clean, or the format pass and your edits become inseparable
sh .claude/skills/fe-style-fix/diagnostics.sh
```

`npm run style` chains its gates with `&&` and stops at the first failure — right for CI's exit
code, wrong as a worklist. `diagnostics.sh` runs all four regardless of each other's result and
prints counts per diagnostic id, then the file:line sites grouped by id. Ids are the tools' own:
`PRETTIER`, an ESLint rule name, a `TSnnnn` code, or an `FEnnn` structure rule. Keep that output —
it is the worklist, and the number you must drive to zero.

One quirk that will otherwise mislead you: `tsc -b` is build mode, and it stays silent for projects
whose `.tsbuildinfo` says they are up to date. A run that prints nothing is not proof of a clean
tree. `diagnostics.sh` passes `--force`; if you invoke `tsc -b` yourself, do the same.

## 1. Deterministic pass

Run the two fixers separately, in this order, and commit each on its own — mixing a formatter's
churn into a commit with real code changes makes both unreviewable.

```
cd FE
npm run format      # Prettier rewrites layout
npm run lint:fix    # ESLint applies every rule that ships a fixer
```

`npm run format` settles `PRETTIER` outright. `lint:fix` clears the mechanical ESLint rules —
`consistent-type-imports`, `prefer-const`, `object-shorthand`, `eqeqeq` — and leaves behind exactly
the rules with no autofix, which is step 3's worklist. Neither tool touches types or structure:
`TSnnnn` and `FEnnn` are always hand work.

Sanity-check the diff before committing (`git diff --stat`, then spot-read a file or two).

## 2. Re-measure

```
sh .claude/skills/fe-style-fix/diagnostics.sh
```

Work the residual list **grouped by diagnostic id, not by file** — the fix for a given id is the
same reasoning every time, and doing all of one id at once keeps it consistent.

## 3. Manual pass — only what has no fixer

### ESLint rules that need a decision

| Rule | What it wants | How to fix |
|---|---|---|
| `@typescript-eslint/no-floating-promises` | a promise is dropped, so a failed write is lost silently | `await` it, or `void` it when the caller genuinely does not wait. `void` is a statement of intent, not a mute button — if the result matters, await. |
| `@typescript-eslint/no-unsafe-*`, `no-explicit-any` | `any` is flowing through typed code | Type the boundary it enters at. In this app that boundary is `src/api` — give the response a type in [FE/src/api/types.ts](../../../FE/src/api/types.ts) and parse into it, rather than casting at the use site. Already off in tests, where the untyped-ness is the thing under test. |
| `no-restricted-imports` | a layer violation, or an import climbing `../../` | See "the shape" below. The fix is to move code, not to loosen the rule. |
| `no-restricted-syntax` (`ExportDefaultDeclaration`) | a default export | Export by name. Config files (`*.config.ts`) and `scripts/**` are exempt because Vite and Vitest read a default export. |
| `react-hooks/*` | a hook called conditionally, or a dependency array that lies | Fix the call site. Never delete a dependency to quiet the rule — add the memo or move the value instead. |
| `no-console` | a stray `console.log` (a warning, not an error) | Delete it. `console.warn` / `console.error` are allowed. |

### `TSnnnn`

Fix the type, not the symptom. `noUnusedLocals` / `noUnusedParameters` are on, so a deliberately
unused binding takes a leading underscore — that is the escape hatch the compiler and
`@typescript-eslint/no-unused-vars` share, and it is the only one.

`verbatimModuleSyntax` is on, so a type-only import must say `import type` (or the inline
`import { type X }` form this repo uses). `lint:fix` writes that for you.

### `FEnnn` — the shape of the app

`structure.config.json` is the single source of truth; both
[FE/scripts/check-structure.mjs](../../../FE/scripts/check-structure.mjs) and the layer rules in
`eslint.config.js` read it and nothing else.

| Id | What it wants | How to fix |
|---|---|---|
| `FE001` | only `.ts`, `.tsx`, `.css` under `src/` | Move the file. Static assets belong in `FE/public/`, docs in the repo's Markdown. |
| `FE002` | `src/` is one level of directories deep | Flatten it. A sub-feature is a sibling directory with its own rank, not a nested folder — nesting hides a module from the layer map. |
| `FE003` | a `.tsx` component file is PascalCase | Rename, and update every import (see the rename rules below). The exceptions are listed as `lowerCaseTsx` in the config: `main.tsx`, `routes.tsx`. |
| `FE004` | a `.ts` module file is camelCase | Rename. |
| `FE005` | `X.module.css` has an `X.tsx` beside it | Usually a rename that left the stylesheet behind: either rename the stylesheet to match, or delete it if the component is gone. |
| `FE006` | a non-module stylesheet lives in `src/styles/` | Make it a CSS module beside its component, or move it to `src/styles/` and import it from `main.tsx`. There is one global stylesheet, `base.css`, and it is imported once. |
| `FE007` | `X.test.ts(x)` has an implementation beside it | Colocate the test with what it covers. `X.<qualifier>.test.tsx` is fine — `Home.layout.test.tsx` covers `Home.tsx`. |
| `FE008` | a new directory under `src/` has a rank | Add it to `layers` in `structure.config.json`, at a rank strictly above everything it imports and below everything that imports it. This is the one edit to that file this skill sanctions, and only for a directory that genuinely exists. |

The layer map is the app's dependency direction, and it is enforced by ESLint on every import:

```
0  api, format          leaves — know nothing about the app above them
1  labels, month        may use rank 0
2  capture, components  may use ranks 0-1
3  routes               may use ranks 0-2
   src/*.tsx            the entry point (main, App, ErrorBoundary) — unrestricted
```

A module may import from its own directory or from a strictly lower rank. Never sideways
(`components` → `capture` is an error even though both are rank 2) and never upward. When a fix
seems to need a sideways import, the shared thing wants to move down a layer — extract it into the
lower directory both sides already depend on. Ask before inventing a new one.

### Renaming rules for this repo

- A rename is not a local edit. Find every reference first (`Grep` the symbol and the module path
  across `FE/src`, tests included) and change them in the same edit set. `tsc` is the safety net,
  not the search.
- Rename the stylesheet with the component: `X.tsx` and `X.module.css` move together, or `FE005`
  fires on the next run.
- Follow the repo's own rules while fixing. In particular, a `decimal`-shaped value crossing the
  API stays the primitive the API sends — never introduce a wrapper type to satisfy a rule.
- Comments explain *why*. If a fix is non-obvious (a `void` on a deliberately unawaited write,
  say), one short prose line; otherwise none.

## 4. Verify

```
cd FE
npm run style       # format:check, lint, typecheck, structure — the CI gate in one command
npm test            # vitest, jsdom
```

`npm test` needs no Docker and no API; the client's tests mock `fetch`. If a test fails after a
rename, it is almost certainly a module path in a `vi.mock` factory — those are strings, so neither
`tsc` nor a rename tool follows them.

Then re-run `sh .claude/skills/fe-style-fix/diagnostics.sh` once to confirm zero.

## 5. Report

State, concretely:

- the baseline counts per id, and the counts after each pass;
- which ids `npm run format` and `npm run lint:fix` cleared versus which you fixed by hand;
- every diagnostic you deliberately did **not** fix, with the reason (a rule that looks wrong for
  this codebase, a fix that needs the user's sign-off — a layer move, a public rename);
- whether `npm run style` is clean and whether the test suite passed.
