---
name: be-editorconfig-fix
description: Clear the code-style and analyzer diagnostics BE/.editorconfig turns on (IDE*, CA*, RS*, VSTHRD*), deterministically first with dotnet format and only then by hand. Use when the BE build fails on TreatWarningsAsErrors, after adding or tightening .editorconfig rules, or when asked to clean up style/analyzer warnings in the .NET backend.
allowed-tools: Bash, Read, Edit, Write, Glob, Grep
---

# Fixing BE .editorconfig diagnostics

`BE/Directory.Build.props` sets `TreatWarningsAsErrors=true` and `EnforceCodeStyleInBuild=true`,
so every rule in [BE/.editorconfig](../../../BE/.editorconfig) at `warning` or above breaks the
build. This skill clears them in one direction: **every diagnostic a tool can fix is fixed by the
tool; you only touch the ones no fixer exists for.**

Never silence a diagnostic to make it go away — no `#pragma warning disable`, no
`[SuppressMessage]`, no lowering a severity in `.editorconfig`. If a rule genuinely does not fit
this codebase, stop and say so; changing the rule is the user's call, not yours.

## 0. Baseline

```
git status --short          # must be clean, or the format pass and your edits become unseparable
sh .claude/skills/be-editorconfig-fix/diagnostics.sh
```

`diagnostics.sh` builds with `TreatWarningsAsErrors=false --no-incremental` so the build runs to
completion and reports *every* site rather than stopping at the first error. It prints counts per
diagnostic id, then the file:line list grouped by id. Keep that output — it is the worklist, and
the number you must drive to zero.

Two build quirks that will otherwise mislead you:

- An incremental `dotnet build` re-emits diagnostics only for projects it rebuilds. A run that
  prints nothing is not proof of a clean tree. Always `--no-incremental`.
- **IDE0005 (unnecessary using) does not run at all** while `GenerateDocumentationFile` is false,
  which it is for this repo. To audit it: `diagnostics.sh --ide0005` (sets the property and mutes
  the ~960 CS1591/CS1587 XML-doc warnings that come with it). Fix what it finds, but never commit
  the property change.

## 1. Deterministic pass — `dotnet format`

Run the three passes separately, in this order, and commit each on its own. The whitespace pass
alone rewrites well over a hundred files (`.editorconfig` asks for `utf-8-bom`, final newlines,
4-space indent); mixing that churn into a commit with real code changes makes both unreviewable.

```
dotnet format whitespace BE/Expenses.sln
dotnet format style      BE/Expenses.sln --severity info
dotnet format analyzers  BE/Expenses.sln --severity info
```

`--severity info` is deliberate: `.editorconfig` sets several rules to `suggestion`, and the
default (`warn`) skips them, so they resurface the next time someone raises a severity.

Read the format output. Lines of the form

```
Unable to fix IDE1006. Code fix NamingStyleCodeFixProvider doesn't support Fix All in Solution.
Unable to fix CA1819. No associated code fix found.
```

are the tool handing you the worklist for step 3. Everything else it fixed itself.

Re-run `diagnostics.sh`. Measured on the current tree, this pass clears IDE2006 (96), IDE0161 (2)
and the import ordering outright, and leaves IDE1006 (20 `s_` renames) plus CA1819 (3) — that
ratio is what "deterministic first" buys: roughly 80% of the work with no judgement calls.

Sanity-check the diff before committing (`git diff --stat`, then spot-read a few files). `dotnet
format` is safe, but a mangled expression-bodied member is easier to catch now than after your own
edits are layered on top.

## 2. Re-measure

```
sh .claude/skills/be-editorconfig-fix/diagnostics.sh
```

Work the residual list **grouped by diagnostic id, not by file** — the fix for a given id is the
same reasoning every time, and doing all of one id at once keeps it consistent.

## 3. Manual pass — only what has no fixer

Known residents of this list, and how they are fixed here:

| Id | What it wants | How to fix |
|---|---|---|
| `IDE1006` "Missing prefix: `s_`" | `private static readonly` fields are `s_camelCase` | Rename. `private static readonly byte[] JpegMagic` → `s_jpegMagic`. There is *no* Roslyn fix-all for naming, so every one is a hand rename. |
| `IDE1006` other forms | interfaces `I`-prefixed, parameters camelCase, constants PascalCase, private instance fields `_camelCase`, async methods `…Async` | Rename per the `dotnet_naming_rule` block in `.editorconfig`. |
| `CA1819` | a property returns an array (callers can mutate the backing store) | Prefer changing the type to `IReadOnlyList<T>` / `ReadOnlyMemory<byte>`. If the array is genuinely the wire shape (an image payload, a hash), say so and leave it for the user to decide — do not suppress it yourself. |
| `CA1859`, `CA1873`, `CA1851`, `CA1822` | perf/shape rules | Apply the concrete change the message names (concrete return type, guarded log-argument evaluation, materialize the enumerable once, `static` member). |
| `VSTHRD200`, `VSTHRD100` | `Async` suffix, no `async void` | Rename / change the return type to `Task`. |

Renaming rules for this repo:

- A rename is not a local edit. Find every reference first (`Grep` the symbol across `BE/`,
  including tests) and change them in the same edit set. `TreatWarningsAsErrors` means a missed
  reference shows up as a hard build failure, not a warning — that is the safety net, but use it
  as confirmation, not as the search.
- **Do not rename public API to satisfy a private-field rule.** IDE1006's `s_` rule applies to
  `private` fields only; if the message points at something `public` or `internal`, re-read which
  naming rule fired.
- **Do not hand-edit generated files.** `BE/Expenses.Infrastructure/Persistence/Migrations/*.cs`
  and `*Designer.cs` are EF Core output; `dotnet format` fixes their style (IDE0161 etc.) and that
  is fine, but if a rule wants a *semantic* change there, leave it and report it — the fix belongs
  in the migration generator's input or in an `.editorconfig` exclusion the user approves.
- Follow the repo's own rules while fixing: `BE/CLAUDE.md` governs. In particular D7 — a `decimal`
  stays a `decimal`; never introduce a wrapper type as a way of satisfying an analyzer.
- Comments explain *why*. If a fix is non-obvious (a `CA1851` materialization, say), one short
  prose line; otherwise none.

## 4. Verify

```
dotnet build BE/Expenses.sln        # TreatWarningsAsErrors is on by default — this must be clean
dotnet test  BE/Expenses.sln
```

`dotnet test` needs Docker for the integration tests (Testcontainers, real PostgreSQL). If Docker
is unavailable, run `dotnet test BE/tests/Expenses.Domain.Tests` and
`dotnet test BE/tests/Expenses.Application.Tests` and say plainly in your report that the
integration suite did not run.

Then re-run `diagnostics.sh --ide0005` once, to confirm the rule that only runs under a
documentation build is clean too.

## 5. Report

State, concretely:

- the baseline counts per id, and the counts after each pass;
- which ids `dotnet format` cleared versus which you fixed by hand;
- every diagnostic you deliberately did **not** fix, with the reason (generated code, a public API
  change that needs the user's sign-off, a rule that looks wrong for this codebase);
- whether the build is clean under `TreatWarningsAsErrors` and which test suites ran.
