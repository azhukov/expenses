## Why

The repository carries two clients over one ledger. The browser client is the one that gets used and the one the product is designed around — it leads with photographing a receipt on a phone, which is where receipts are. The Avalonia desktop client exists to offer the same flow from a file or a drop, and every change to the ledger's behaviour now has to be made twice: the unit rule just added in `require-purchase-entry-fields` landed in the API and the browser client and left the desktop form behind, which is exactly the tax this removal ends.

It is also a third of the CI matrix — `ui-style`, `ui-unit` and `ui-e2e`, the last of which stands up the whole stack — for a client nobody opens.

## What Changes

- **The desktop client is deleted.** `Avalonia-UI/` goes entirely: the two projects, its three test suites, its own solution, `Directory.Build.props`, `global.json`, `.editorconfig` and README. The code stays recoverable through git history; no tag, branch or attic directory is kept.
- **BREAKING (developer workflow): `./test-ui.sh` is removed**, and `./up.sh` stops building and launching the desktop client. `NO_DESKTOP=1` goes with it rather than staying as an accepted no-op.
- **CI loses three jobs.** `ui-style`, `ui-unit` and `ui-e2e` are removed, along with the `ui` output of the `changes` filter job and the `ui` entries in `gate`'s `needs`. The filter's rule that `Avalonia-UI/` or `test-ui.sh` makes a change "a half of its own" goes too.
- **The `desktop-client` capability is retired.** All 18 of its requirements are removed, so archiving this change retires the spec rather than leaving it describing something that does not exist.
- **Documentation stops describing two clients.** The root `README.md` (its opening description, the test table, the local-runner list and the `./up.sh` paragraph) and the one line in `BE/README.md` that names the desktop client among the callers of plain HTTP.
- **Not changed: the ledger.** No endpoint, tool, schema or behaviour of the API moves. The desktop client was a consumer of the HTTP interface and nothing in the interface existed only for it.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `desktop-client`: every requirement is removed. The capability described a client that will no longer exist, and a spec is a description of the system rather than a wish for it.
- `api-surface`: one requirement's wording names the desktop client as an example of a caller that sends no `Origin` header. The requirement itself does not change; the example does, because it would otherwise point at something removed.

## Impact

- **Deleted:** `Avalonia-UI/` (including `TestResults/`), `test-ui.sh`.
- **Changed:** `.github/workflows/ci.yml` (three jobs, the `changes` filter, the `gate` job's `needs`, and the header comment that lists what runs where), `up.sh`, `README.md`, `BE/README.md`, `openspec/specs/desktop-client/spec.md` (retired on archive).
- **Untouched:** everything in `BE/` and `FE/`, the compose files, and `test-be.sh`, `test-fe.sh`, `test-e2e.sh`. `docker-compose.e2e.yml` is shared with the browser client's e2e suite and stays exactly as it is.
- **The `gate` check keeps reporting.** It is the only required check on `main`, so its `needs` list must be edited in the same change that removes the jobs it names — a `needs` entry pointing at a job that no longer exists is a workflow that will not run at all.
- **The follow-up flagged by `require-purchase-entry-fields` disappears with the client.** That change noted the desktop form still does not require a unit on an expense line; there is now nothing to bring to parity.
