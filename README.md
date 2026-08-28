# expenses

A personal expense ledger: a .NET backend in [BE/](BE) with two front doors — HTTP and MCP — over
one Application layer, and a browser client in `FE/`.

## Running locally

```bash
docker compose up -d --build
```

| | | |
| --- | --- | --- |
| API | <http://localhost:5082> | Swagger UI at `/swagger` |
| MCP | <http://localhost:5083> | HTTP transport |
| PostgreSQL | `localhost:5432` | user, password and database all `expenses` |

`docker compose up -d postgres` brings up just the database, which is all the test suite and a
locally-run host need. See [BE/README.md](BE/README.md) for what each service does and why the MCP
container waits on the API's healthcheck, and
[BE/Expenses.Mcp/README.md](BE/Expenses.Mcp/README.md) for the MCP tool surface.
