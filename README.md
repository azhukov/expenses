# expenses

A personal expense ledger: a .NET backend in [BE/](BE) with two front doors — HTTP and MCP — over
one Application layer, and a browser client in [FE/](FE) that leads with photographing a receipt.

## Running locally

```bash
docker compose up -d --build
```

| | | |
| --- | --- | --- |
| API | <http://localhost:5082> | Swagger UI at `/swagger` |
| MCP | <http://localhost:5083> | HTTP transport |
| PostgreSQL | `localhost:5432` | user, password and database all `expenses` |

The browser client is not in `docker compose`: it is run from `FE/` with `npm install && npm run
dev` on <http://localhost:5173>, and reaches the API through a dev proxy. How the built client is
served in production has deliberately not been decided yet — see [FE/README.md](FE/README.md).

## Tests, and the gate

Every change reaches `main` through a pull request, and one required check — `gate` in
[.github/workflows/ci.yml](.github/workflows/ci.yml) — decides whether it may. That check waits on
every suite the repository has:

| | | |
| --- | --- | --- |
| unit | `BE/tests/Expenses.Domain.Tests`, `Expenses.Application.Tests` | no database, no containers |
| unit | `FE` vitest | jsdom, `fetch` mocked |
| integration | `BE/tests/Expenses.Integration.Tests` | the real host over a real PostgreSQL through Testcontainers (D17) |
| end-to-end | `FE/e2e` | a browser, the built client, the real API, a real database |
| style | both halves | `BE/.editorconfig` at warning and above; Prettier, ESLint, tsc and the `src/` shape rules in `FE` |

A pull request touching only one half skips the other half's jobs, and `gate` still reports, so a
skipped suite never leaves a PR waiting forever on a check that will not run.

Locally:

```bash
./test-fe.sh               # the client unit suite, nothing running required
./test-e2e.sh              # the end-to-end suite, stack and all
./test-be.sh               # every BE suite, with merged coverage in the console
```

`./up.sh` brings up the local stack — postgres, api, mcp — waits for every healthcheck, then runs
and opens the browser client. Run with `NO_FE=1` for the containers alone, `COMPOSE_BUILD=1` to
rebuild the api and mcp images first, or `FE_HOST=1` to expose the client on the LAN.

`docker compose up -d postgres` brings up just the database, which is all the test suite and a
locally-run host need. See [BE/README.md](BE/README.md) for what each service does and why the MCP
container waits on the API's healthcheck, and
[BE/Expenses.Mcp/README.md](BE/Expenses.Mcp/README.md) for the MCP tool surface.

Every script prints its own options in a header comment — read the top of the file for the full
list of environment variables it accepts (`UNIT`, `DETAIL`, `WATCH`, `KEEP`, `HEADED`,
`SKIP_BUILD`, `NO_FE`, `COMPOSE_BUILD`, `FE_HOST`). On Windows, run them from a Git Bash terminal.
