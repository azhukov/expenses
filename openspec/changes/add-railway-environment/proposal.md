## Why

The ledger runs only on a developer machine today. The API and the browser client now each have a Railway service: the API is `expenses-api` (private `expenses-api.railway.internal`, public `https://expenses-api-production-4d79.up.railway.app`) and the client is `https://expenses-frontend-production-2e52.up.railway.app`. Nothing in the repo tells either host how to run there. The API allows no browser origin and applies no migrations outside `Development`. The client has no production server at all ("choosing one is still open").

## What Changes

- The API gains a `Railway` environment (`ASPNETCORE_ENVIRONMENT=Railway`, read from `appsettings.Railway.json`). It allows exactly `https://expenses-frontend-production-2e52.up.railway.app` as a browser origin and applies migrations when it starts.
- Applying migrations on startup becomes a setting (`Database:ApplyMigrationsOnStartup`) instead of being tied to `Development`. `Development` keeps migrating, and every other environment keeps not migrating unless it opts in.
- The browser calls the API at its **public** domain. `expenses-api.railway.internal` is reachable only from inside Railway's private network, and the client has no proxy by design, so the client's `API_URL` on Railway is `https://expenses-api-production-4d79.up.railway.app`.
- The client gets a Dockerfile that builds the bundle and serves it with `vite preview` on Railway's `$PORT`. `vite preview` already writes `/config.js` from `API_URL`.
- The preview server accepts requests for the host names it is told it is served under (`PREVIEW_ALLOWED_HOSTS`). Vite refuses unknown `Host` headers by default, so without this the Railway domain would get a 403.
- Railway config-as-code files for both services, plus a README section that lists every variable, the volume and the one-time database provisioning.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `api-surface`: adds a requirement that migrating on startup is a configurable setting, and a requirement for the `Railway` environment's allowed origin and migrations.
- `browser-client`: adds a requirement that the preview server answers the public host names it is configured with, and refuses others.

## Impact

- **BE**: `Program.cs` (the migration condition), new `appsettings.Railway.json`, new `BE/railway.json`, integration tests for the setting and the Railway origin, `BE/README.md`.
- **FE**: `vite.config.ts` (`preview.allowedHosts` from the environment, parsed by a pure function with unit tests), new `FE/Dockerfile` and `FE/.dockerignore`, new `FE/railway.json`, `FE/README.md` ("Serving the built client" is no longer open).
- **Railway (manual, outside the repo)**: service variables, a volume for receipts, public domains, and creating the `expenses` database once with `BE/db/init/01-create-database.sql`.
- **Security**: the API has no authentication and now gets a public URL. CORS limits which browser pages can read it, but it does not stop direct calls. This is accepted for now and recorded as a risk. Authentication stays out of scope.
- **Out of scope**: the MCP host on Railway, the desktop client, custom domains, CI deployment.
