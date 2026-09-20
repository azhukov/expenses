## Context

See proposal.md — Why. What shapes the approach:

- **`gate` is the only required check on `main`.** It lists every suite in `needs:` and passes when each is `success` or `skipped`. A `needs:` entry naming a job that no longer exists is not a skipped job — GitHub refuses to run the workflow at all, and a required check that never reports is a pull request that can never merge. That failure mode is the reason the workflow header exists in the first place.
- **The `changes` filter decides what runs.** It sets three outputs (`fe`, `be`, `ui`) from a `git diff`, treats `Avalonia-UI/` and `test-ui.sh` as a half of their own, and falls back to running everything when there is no usable base commit.
- **`ui-e2e` runs on a BE change as well**, because a changed contract would break the desktop client and that suite is what would notice. `e2e` (the browser one) already covers the same contract the same way, over `docker-compose.e2e.yml`.
- **The desktop client is self-contained.** Its own solution, its own `Directory.Build.props`, `global.json` and `.editorconfig` — copies of BE's rather than BE's own, deliberately, so neither half's build depends on the other. Nothing in `BE/` or `FE/` references it.
- **`up.sh` builds it with `dotnet build`, launches it with `nohup`, and logs to a temp file.** `NO_DESKTOP=1` skips that path. `test-ui.sh` is its own runner, sharing only `docker-compose.e2e.yml` with the browser suite.

## Goals / Non-Goals

**Goals:**

- A repository that, afterwards, contains no reference to a desktop client — not a dangling script, a `needs:` entry, a README row or a spec.
- A workflow that still parses, still runs, and still reports `gate` on the first pull request after the removal.

**Non-Goals:**

- Any change to the ledger, the browser client, or the compose files.
- Preserving the deleted code anywhere but git history: no tag, no branch, no `attic/`.
- Replacing what the desktop client offered — a file or a drop as the capture source — in the browser client. If that turns out to be wanted, it is its own change against `browser-client`.

## Decisions

**D1 — Delete in one change, verified by a search rather than by memory.**
The removal is mechanical but wide: seven files or directories deleted, five edited, across a workflow, two shell scripts, two READMEs and two specs. The check that it is complete is a repository-wide search for `Avalonia`, `desktop` and `test-ui` that returns only the archived OpenSpec changes and the unrelated `claude_desktop_config.json` line in `BE/Expenses.Mcp/README.md`. Rejected: deleting the directory first and cleaning up references as CI complains, which leaves the repository broken between steps and relies on CI to find prose that CI does not read.

**D2 — The workflow is edited before the directory is deleted.**
`ui-e2e` runs `bash ./test-ui.sh`. If the script is deleted while the job still exists, any push in between produces a workflow whose job fails on a missing file. Removing the three jobs, the `gate` `needs:` entries, the `changes` output and the header comment first means the workflow never names something that is not there. Within one commit the ordering is invisible, but the change is likely to be reviewed as a diff and the two must not be split across commits in the other order.

**D3 — The `ui` output leaves the `changes` job entirely.**
Not kept as a constant `false`. An output nothing reads is a thing the next reader has to work out the meaning of, and the fallback branch that sets every output to `true` would keep asserting something about a half that does not exist. The `shared` rule stays as it is: `ci.yml`, the compose files and `test-e2e.sh` still affect both surviving halves.

**D4 — `NO_DESKTOP=1` is dropped rather than accepted and ignored.**
A flag that silently does nothing is worse than an error: it reads as though it still controls something. Anyone whose shell history carries it gets `up.sh`'s own usage rather than a false sense that a client was skipped. The script's header comment is where the removal is announced, since every script in this repository documents itself there.

**D5 — The `desktop-client` delta removes each requirement by name, with one shared reason.**
`openspec validate` wants a **Reason** and a **Migration** on a removed requirement. Stating the reason once at the top and pointing each requirement at its `browser-client` counterpart keeps eighteen entries readable, and makes the two that have *no* counterpart — "Capture takes an image from a file or a drop" and "The client fits the window it is shown in" — visible as the genuine losses they are rather than hiding them in a uniform list.

**D6 — `api-surface` is modified rather than left alone.**
One sentence of "Browser origins are allowed by configuration" names the desktop client as an example of a caller sending no `Origin` header. The rule does not change and neither do its scenarios; only the example does. The whole requirement is restated in the delta because a MODIFIED requirement must carry its full content, and the integration test named after the surviving scenario keeps passing untouched.

## Risks / Trade-offs

- **[A `needs:` entry left pointing at a deleted job silently disables CI.]** → D2, and the verification is the first pull request after the change: `gate` must appear and report. This cannot be checked locally, so it is the one thing to watch on the PR itself rather than before pushing.
- **[The contract coverage `ui-e2e` gave is lost.]** → Accepted, and it was duplicate: `e2e` exercises the same HTTP interface against the same stack from the browser client. The loss is real only for behaviour the desktop client had and the browser client does not, which is behaviour that is also being deleted.
- **[Someone wants the desktop client back.]** → It is in git history, its interface is unchanged, and this change's spec delta records what it did. Rebuilding it would be a new change, not a revert, since the ledger will have moved on.
- **[A reference survives the sweep.]** → The search in D1 is the check, and it is cheap to repeat. The likely survivors are prose rather than code: a README table row, a comment in a script, a line in the CI header.

## Migration Plan

No data, no deployment and no runtime change: nothing in this removal reaches the API, the database or the deployed browser client. A developer with the repository checked out loses `./test-ui.sh` and the desktop window `./up.sh` used to open; `git pull` is the whole migration.

Rollback is `git revert`, which restores the directory, the script, the three jobs and the spec together.
