# expenses

A personal expense ledger: a .NET backend in [BE/](BE) with two front doors — HTTP and MCP — over
one Application layer, a browser client in [FE/](FE) that leads with photographing a receipt, and a
desktop client in [Avalonia-UI/](Avalonia-UI) that offers the same flow from a file or a drop.

## Running locally

```bash
docker compose up -d --build
```

| | | |
| --- | --- | --- |
| API | <http://localhost:5082> | Swagger UI at `/swagger` |
| MCP | <http://localhost:5083> | HTTP transport |
| PostgreSQL | `localhost:5432` | user, password and database all `expenses` |

The browser client is not in `docker compose`: it is run from `FE/` with `npm install` and
`API_URL=http://localhost:5082 npm run dev` on <http://localhost:5173>, and calls the API directly,
which allows that origin. `./up.sh` sets `API_URL` for you; `FE_HOST=1 ./up.sh` also serves the
page and the API over HTTPS on the LAN for a phone. The deployed client and API run on Railway: the
client is `vite preview` from [FE/Dockerfile](FE/Dockerfile), and the API runs as the `Railway`
environment — see [BE/README.md](BE/README.md#railway).

The desktop client is not in `docker compose` either: `dotnet run --project
Avalonia-UI/src/Expenses.Desktop` opens it against the API on port 5082 — see
[Avalonia-UI/README.md](Avalonia-UI/README.md).

## Tests, and the gate

Every change reaches `main` through a pull request, and one required check — `gate` in
[.github/workflows/ci.yml](.github/workflows/ci.yml) — decides whether it may. That check waits on
every suite the repository has:

| | | |
| --- | --- | --- |
| unit | `BE/tests/Expenses.Domain.Tests`, `Expenses.Application.Tests` | no database, no containers |
| unit | `FE` vitest | jsdom, `fetch` mocked |
| unit | `Avalonia-UI` view models and headless UI | no display, the ledger faked at the transport |
| integration | `BE/tests/Expenses.Integration.Tests` | the real host over a real PostgreSQL through Testcontainers (D17) |
| end-to-end | `FE/e2e` | a browser, the built client, the real API, a real database |
| end-to-end | `Avalonia-UI/tests/Expenses.Desktop.E2E.Tests` | the headless desktop client, the real API, a real database |
| style | every half | `BE/.editorconfig` at warning and above, and its copy in `Avalonia-UI`; Prettier, ESLint, tsc and the `src/` shape rules in `FE` |

A pull request touching only one half skips the other halves' jobs, and `gate` still reports, so a
skipped suite never leaves a PR waiting forever on a check that will not run.

Locally:

```bash
./test-fe.sh               # the client unit suite, nothing running required
./test-e2e.sh              # the end-to-end suite, stack and all
./test-be.sh               # every BE suite, with merged coverage in the console
./test-ui.sh               # the desktop client's view-model and headless UI suites; E2E=1 for end-to-end
```

`./up.sh` brings up the local stack — postgres, api, mcp, rebuilding the api and mcp images every
time — waits for every healthcheck, starts the desktop client in a window of its own, then runs and
opens the browser client. Run with `NO_DESKTOP=1` or `NO_FE=1` to skip either client (both for the
containers alone), or `FE_HOST=1` to expose the browser client on the LAN over HTTPS, at
`https://<machine-name>.local:5173`.

`docker compose up -d postgres` brings up just the database, which is all the test suite and a
locally-run host need. See [BE/README.md](BE/README.md) for what each service does and why the MCP
container waits on the API's healthcheck, and
[BE/Expenses.Mcp/README.md](BE/Expenses.Mcp/README.md) for the MCP tool surface.

Every script prints its own options in a header comment — read the top of the file for the full
list of environment variables it accepts (`UNIT`, `DETAIL`, `WATCH`, `KEEP`, `HEADED`,
`SKIP_BUILD`, `NO_FE`, `NO_DESKTOP`, `FE_HOST`, `E2E`). On Windows, run them from a Git Bash terminal.
