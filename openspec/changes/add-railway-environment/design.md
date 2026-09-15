## Context

- The client runs in the browser and calls the API directly. `API_URL` is served to the page as `/config.js` by the server that serves the client, and there is no proxy (archived `configure-api-url-and-cors`, D1/D2). Any address in `API_URL` must therefore be reachable **from the user's browser**. `expenses-api.railway.internal` is Railway's private network, and only other Railway services can resolve it. So the client uses the API's public domain, `https://expenses-api-production-4d79.up.railway.app`, and the internal name is kept for service-to-service use such as a future MCP host.
- CORS is bound from `Cors:AllowedOrigins`. `appsettings.json` lists none (fail closed), and `appsettings.Development.json` lists the local dev and preview origins. The binding validates each entry at startup.
- [Program.cs](../../../BE/Expenses.Api/Program.cs) calls `PrepareExpensesDatabase(applyMigrations: app.Environment.IsDevelopment())` in effect. The OpenAPI document and Swagger UI are also mapped only in Development. The provisioning check (ICU `und` collation, D13) runs everywhere.
- The API image ([BE/Expenses.Api/Dockerfile](../../../BE/Expenses.Api/Dockerfile)) builds from context `BE/`, listens on 8080 (`ASPNETCORE_HTTP_PORTS` default in the aspnet image), and runs as `$APP_UID`. It creates `/var/lib/expenses/receipts` and `receipts-temp`.
- The client builds with `tsc -b && vite build`. `vite preview` serves `dist/` and `/config.js`. `vite.config.ts` imports `@vitejs/plugin-react`, `@vitejs/plugin-basic-ssl` and `vitest/config`, all of which are devDependencies, so running `vite preview` needs the full dev install.
- Vite's preview server checks the `Host` header against `preview.allowedHosts` (DNS-rebinding protection). It allows `localhost` and IPs only, so a request for `expenses-frontend-production-2e52.up.railway.app` would get a 403.
- Railway injects `PORT`, terminates TLS at its edge and forwards plain HTTP. Each service in a monorepo has a root directory. A config-as-code file does **not** follow the root directory, so its path must be set per service as an absolute repo path.

## Goals / Non-Goals

**Goals:**

- `git push` plus a Railway redeploy brings up a working API and client, with no code values to edit per deploy.
- Everything Railway-specific that belongs in code is committed: an environment settings file, Dockerfiles and `railway.json` files. Everything secret or instance-specific is a Railway variable, and the README lists each one.
- Local development, compose, e2e and CI behave exactly as they do today.

**Non-Goals:**

- A proxy, or any use of `*.railway.internal` from the browser path.
- Authentication, rate limiting, or hiding Development-only controllers from the public API. See Risks.
- The MCP host on Railway, deploying from CI, custom domains, several replicas.

## Decisions

### D1 — A named `Railway` environment, not overrides on `Production`

`ASPNETCORE_ENVIRONMENT=Railway` loads `appsettings.Railway.json` on top of `appsettings.json`. The file holds:

```json
{
  "Cors": { "AllowedOrigins": [ "https://expenses-frontend-production-2e52.up.railway.app" ] },
  "Database": { "ApplyMigrationsOnStartup": true }
}
```

**Why:** the user asked for a Railway environment. A committed file makes the allowed origin reviewable and testable: an integration test starts the host with `Environment = "Railway"` and asserts the origin. With Railway variables alone (`Cors__AllowedOrigins__0`), a typo would only show up as a browser refusing every call. `IsDevelopment()` stays false, so the OpenAPI and Swagger mapping and the Development-only conveniences remain off.

**Alternative rejected:** `Production` plus `Cors__AllowedOrigins__0` and friends as Railway variables. It needs no new file, but nothing in the repo would record or test what Railway runs with.

Secrets and instance details are **not** in the file. `ConnectionStrings__Expenses`, `Extraction__Vision__ApiKey` and the receipt paths stay Railway variables, as they are compose variables today.

### D2 — `Database:ApplyMigrationsOnStartup` replaces the hard-wired `IsDevelopment()` for migrating

`Program.cs` computes `bool applyMigrations = app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup")`. `PrepareExpensesDatabase` is then called once with that value, outside the `if`. The OpenAPI and Swagger mapping stays inside `if (IsDevelopment())`. The value is a plain `bool` read from configuration, with no options class.

**Why:** the user chose "API migrates on start". With a single API replica, the race D15 warned about (two hosts migrating) does not occur: the Railway MCP host is out of scope, and any MCP host never migrates. Making it a setting keeps "other environments do not migrate" as the default, and lets a test pin both sides.

**Alternative rejected:** a Railway pre-deploy command running an EF migrations bundle. It is cleaner about DDL rights, but it needs a second image or the SDK in the runtime image. The user chose the simpler path. `BE/README.md` "Migrations" is updated to say Railway opts in.

**Test seam:** `ExpensesApi` already accepts an environment and configuration pairs. The scenarios run against a fresh, provisioned but unmigrated database from the integration fixture, and assert whether the `__EFMigrationsHistory` rows exist after startup.

### D3 — The client is served by `vite preview` from `FE/Dockerfile`

```
FROM node:22-slim
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build
ENV NODE_ENV=production
CMD ["sh", "-c", "exec node node_modules/vite/bin/vite.js preview --host 0.0.0.0 --port ${PORT:-4173} --strictPort"]
```

`FE/.dockerignore` excludes `node_modules`, `dist`, `test-results`, `.env*` and `e2e`.

**Why `vite preview`:** it already implements the `/config.js` contract from `API_URL`, including validation that stops the server. The e2e suite exercises it on every CI run, so no second host has to reimplement and test the contract. The load is one user, which is well within what it can serve.

**Why a single stage with the dev install:** `vite.config.ts` needs its devDependencies when the preview server loads it. Pruning would mean splitting the config, and would save image size only.

**Alternatives rejected:**

- *Static host (Caddy, nginx, Railpack static)* plus an entrypoint that writes `dist/config.js` from `API_URL`. This gives a smaller image, but it reimplements address validation in shell, and the scenario "a malformed address stops the server" would need a second test.
- *Build-time `VITE_API_URL`.* This was already rejected in the archived design, because it breaks the "different address needs no rebuild" requirement.

### D4 — `preview.allowedHosts` comes from `PREVIEW_ALLOWED_HOSTS`, parsed by a pure function

A new `src/api/previewHosts.ts` exports `parsePreviewHosts(value: string | undefined): string[]`. It splits on commas, trims, and drops empty entries. It throws, naming `PREVIEW_ALLOWED_HOSTS`, on any entry containing `/`, `:` or whitespace. The module sits next to `ledgerAddress.ts`, has a colocated vitest test, and must stay browser-free like its neighbour. `vite.config.ts` sets `preview: { allowedHosts: parsePreviewHosts(process.env.PREVIEW_ALLOWED_HOSTS ?? env.PREVIEW_ALLOWED_HOSTS) }`. Only `preview` gets this setting, and the dev server keeps Vite's default.

On Railway the variable is set to `${{RAILWAY_PUBLIC_DOMAIN}},healthcheck.railway.app`, so a regenerated domain follows automatically. The second name is there because Railway sends its healthcheck with `Host: healthcheck.railway.app`, which would otherwise get a 403. The spec scenario uses the literal current domain.

**Why not `allowedHosts: true`:** it would switch off the protection for every preview server, including the one e2e runs, and nothing in the repo would say which host is expected.

**Why not read `RAILWAY_PUBLIC_DOMAIN` directly in `vite.config.ts`:** that would put a vendor name in shared config. One generic variable, set by reference in Railway, keeps `vite.config.ts` host-agnostic.

**Where the wiring is proven:** the pure function is unit-tested against the list scenarios. The "listed host is served / unlisted refused" scenarios are checked by an e2e-style check. It is a small node script or Playwright test that starts `vite preview` with `PREVIEW_ALLOWED_HOSTS=expenses-frontend-production-2e52.up.railway.app`, requests `/` with that `Host` header (expecting 200) and with `Host: elsewhere.test` (expecting 403), then fetches `/config.js`.

### D5 — Railway config-as-code

`BE/railway.json`:

```json
{
  "$schema": "https://railway.com/railway.schema.json",
  "build": { "builder": "DOCKERFILE", "dockerfilePath": "Expenses.Api/Dockerfile" },
  "deploy": { "healthcheckPath": "/units", "healthcheckTimeout": 120, "restartPolicyType": "ON_FAILURE" }
}
```

`FE/railway.json` is the same with `dockerfilePath: "Dockerfile"` and `healthcheckPath: "/config.js"`. A client whose address is broken never starts, so it never passes this healthcheck.

Service settings, done by hand once and documented:

| Service | Root directory | Config file | Variables |
| --- | --- | --- | --- |
| `expenses-api` | `/BE` | `/BE/railway.json` | `ASPNETCORE_ENVIRONMENT=Railway`, `ASPNETCORE_HTTP_PORTS=8080`, `PORT=8080`, `ConnectionStrings__Expenses=Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=expenses;Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}}`, `Receipts__RootPath=/var/lib/expenses/receipts`, `TemporaryReceipts__RootPath=/var/lib/expenses/receipts-temp`, `Extraction__Vision__ApiKey` (secret), `RAILWAY_RUN_UID=0` |
| `expenses-frontend` | `/FE` | `/FE/railway.json` | `API_URL=https://expenses-api-production-4d79.up.railway.app`, `PREVIEW_ALLOWED_HOSTS=${{RAILWAY_PUBLIC_DOMAIN}},healthcheck.railway.app` |

The API also gets a volume mounted at `/var/lib/expenses/receipts`.

**`/units` as the API healthcheck:** `/openapi/v1.json` is mapped only in Development. `/units` is a cheap read that needs the database, and Kestrel listens only after `PrepareExpensesDatabase`. So a passing healthcheck means the host migrated and verified provisioning.

**`RAILWAY_RUN_UID=0`:** Railway mounts volumes owned by root. The image's `chown` does not seed a Railway volume the way Docker seeds a named volume, so without this the host would refuse to start on a non-writable receipt store. Running as root is the documented Railway remedy.

### D6 — The `expenses` database is created once, by hand, with the existing script

Railway's Postgres service creates a database called `railway` with the image defaults. The provisioning check would (correctly) refuse that database. Before the first API deploy, the README step runs `BE/db/init/01-create-database.sql` against the Railway Postgres maintenance database (`railway connect Postgres`, or `psql` with the public TCP proxy URL), and the connection string names `Database=expenses`.

**Why by hand:** the script creates a database, which cannot be done from inside a migration (D13), and it runs once for the life of the service. Automating it would give the runtime host `CREATEDB` rights for a one-time act.

## Risks / Trade-offs

- **[The API is public and unauthenticated]** → Anyone who finds `expenses-api-production-4d79.up.railway.app` can read and write the ledger, and CORS only constrains browsers. This includes whatever `DiagnosticsController` exposes. Mitigation: none in this change beyond not advertising the URL, because authentication is a separate change. The proposal records it, and the README's "no authentication" note is reworded from "private network" to say this plainly.
- **[Railway domain regenerated or renamed]** → The origin in `appsettings.Railway.json` would stop matching, and the browser would refuse every call. `PREVIEW_ALLOWED_HOSTS` follows by reference, but CORS does not. Mitigation: a README note, plus `Cors__AllowedOrigins__0` as a Railway-variable override (index 0 replaces the file's entry) for use until the file is updated.
- **[Wrong connection string format]** → Railway's `DATABASE_URL` is a URI, and Npgsql does not accept it as a connection string. The README gives the key/value template built from `PGHOST` and the other variables.
- **[Migrating at runtime needs DDL rights]** → The Railway Postgres user owns the database, so this works. It is the trade-off the user accepted (D2).
- **[`vite preview` is not a hardened production server]** → Acceptable for a single user. Replacing it only has to honour `/config.js` and the host-name rule, both of which are now specified.
- **[Image rebuild on every FE push installs dev dependencies]** → Slower builds, but no runtime impact.
- **[Railway routes to the wrong port]** → `PORT=8080` on the API aligns Railway's routing with Kestrel's port. The FE reads `$PORT` directly.

## Migration Plan

1. Merge the BE changes (the setting, the Railway settings file, `railway.json`) and the FE changes (host list, Dockerfile, `railway.json`). Local and CI behaviour is unchanged.
2. In Railway, create the `expenses` database with the script (D6), add the API volume, set both services' root directory, config path and variables (D5), and generate or confirm the public domains.
3. Deploy the API and wait for `/units` to report healthy. Then deploy the client, open `https://expenses-frontend-production-2e52.up.railway.app`, and confirm the home screen loads purchases with no CORS error in the console.

Rollback: redeploy the previous Railway deployment. The migration setting defaults to off, so reverting the code does not affect local environments. An applied migration is not rolled back automatically, which is the same as today.
