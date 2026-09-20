Every task here deletes code, edits a workflow, a script or documentation, or verifies that what
remains still runs. None of them changes the behaviour of the ledger or of the surviving client, so
none is preceded by a failing test: there is no new behaviour to specify. The suites that already
exist are the check, and section 4 runs them.

## 1. CI first, so the workflow never names what is gone

- [x] 1.1 In `.github/workflows/ci.yml`, delete the `ui-style`, `ui-unit` and `ui-e2e` jobs, including the comments above `ui-style` and `ui-e2e` that explain why each exists (D2).
- [x] 1.2 Remove `ui-style`, `ui-unit` and `ui-e2e` from the `gate` job's `needs:` list. Leave the job's body alone — it reads whatever `needs` holds. A leftover entry here stops the whole workflow from running, which is the one failure this change must not ship (D1, and the risk in design.md).
- [x] 1.3 In the `changes` job, delete the `ui:` output, the `echo "ui=true"` in the no-usable-base branch, the `grep -Eq '^(Avalonia-UI/|test-ui\.sh$)'` rule with its comment, and the final `echo "ui=$ui"` (D3). Leave the `shared` rule and the `fe`/`be` lines untouched.
- [x] 1.4 In the workflow's header comment, delete the three `ui-*` lines from "What runs where" and the trailing clause of the last paragraph that offers `./test-ui.sh` for the desktop client.

## 2. Delete the client and its runner

- [x] 2.1 Delete `Avalonia-UI/` entirely, `TestResults/` included. Confirm afterwards that `git status` shows the deletion and no stray untracked leftovers under that path.
- [x] 2.2 Delete `test-ui.sh`.

## 3. Scripts, documentation and specs

- [x] 3.1 In `up.sh`, remove the desktop path: the `desktop` and `desktop_log` variables, the build and `nohup` launch, the "Desktop started" line, the `NO_DESKTOP` branch, and the mentions of the desktop client in the header comment — including the sentence about it outliving the script and the one about Ctrl+C leaving the desktop window up (D4). Check the remaining header still describes what the script now does.
- [x] 3.2 In `README.md`: drop the desktop client from the opening description, the paragraph beginning "The desktop client is not in `docker compose` either", the two `Avalonia-UI` rows of the suite table, the `Avalonia-UI` clause of the style row, the `./test-ui.sh` line in the local-runner block, and the `./up.sh` paragraph's mention of the desktop window and `NO_DESKTOP=1`.
- [x] 3.3 In `BE/README.md`, edit the line that explains why plain HTTP is never redirected so it no longer names the desktop client among the reasons. Leave the `claude_desktop_config.json` mention in `BE/Expenses.Mcp/README.md` alone — it is an MCP client's config file, nothing to do with this.
- [x] 3.4 Confirm the two spec deltas in this change still match what was actually removed: all 18 `desktop-client` requirements, and the one sentence of `api-surface`'s "Browser origins are allowed by configuration" (D5, D6). They are written already; this task is reading them against the final diff, not writing them.

## 4. Verify

- [x] 4.1 Search the repository for `Avalonia`, `test-ui` and `desktop`, case-insensitively, excluding `.git/`, `node_modules/`, `obj/`, `bin/` and `openspec/changes/archive/`. The only surviving hits should be this change's own artifacts, the `openspec/specs/desktop-client/spec.md` the archive step will retire, and `claude_desktop_config.json` in the MCP README. Anything else is a reference the sweep missed (D1).
- [x] 4.2 Run `./test-be.sh` and `./test-fe.sh`. Both should be exactly as green as before — nothing they cover was touched. Report what each returned.
- [x] 4.3 Run `./test-e2e.sh`. This is the suite that shares `docker-compose.e2e.yml` with the deleted one, so it is the proof the shared file was left intact. Note that it publishes port 5082 and needs the development API stopped first.
- [x] 4.4 Confirm the workflow still parses before pushing: `gh workflow view CI` or a YAML parse of `.github/workflows/ci.yml`. A structural mistake in `needs:` will not show up in any local suite.
- [ ] 4.5 On the pull request, confirm `gate` appears as a check and reports. This cannot be checked locally and is the one risk this change carries (design.md, Risks).
