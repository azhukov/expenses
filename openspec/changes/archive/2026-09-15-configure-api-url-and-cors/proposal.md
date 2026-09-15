## Why

The browser client can only reach the API through Vite's `/api` proxy. The client hard-codes `const BASE = '/api'`, the proxy target is fixed in `vite.config.ts`, and the API has no CORS configuration. As a result, nothing but the Vite dev and preview servers can serve the client, and `FE/README.md` records that `npm run build` "produces a bundle nothing serves". This change makes the API address a setting and lets the API decide which browser origins may call it. With that in place, the client no longer depends on a proxy to be same-origin.

## What Changes

- **FE: the API address comes from an environment variable read when the FE server starts.** `API_URL` (e.g. `http://localhost:5082`) is read when `vite` or `vite preview` starts. It is handed to the page at runtime rather than compiled into the bundle, so one build can point at any API. If the variable is missing or is not an absolute http(s) URL, the server refuses to start and names the variable.
- **FE: requests go straight to the configured address.** `client.ts` and `writes.ts` stop prefixing `/api` and build every URL from the configured address.
- **BREAKING (FE dev workflow): the `/api` proxy is removed** from both `server` and `preview` in `vite.config.ts`. `E2E_API_URL` is replaced by `API_URL`. Running the client now requires `API_URL`, and the API must allow the client's origin.
- **API: cross-origin browser calls are allowed from configured origins only.** `Cors:AllowedOrigins` lists exact origins. If the list is empty or absent, no cross-origin call is allowed, which fails closed. An entry that is not a plain http(s) origin (a wildcard, a path, a non-http scheme) stops the host at startup.
- **API: error responses stay readable cross-origin.** An `ErrorResponse` for a 400, 404, 409 or 500 carries the same CORS headers as a success, so the client keeps showing the ledger's own error message rather than a generic network failure.
- **API: HTTPS for phone testing on a LAN.** The page a phone opens is `https://<machine>.local:5173`. Without the proxy, that page calls the API directly, which only works if the API is also served over HTTPS at a LAN address. The API gains HTTPS driven purely by configuration: a certificate and an HTTPS port, with plain HTTP kept alongside and never redirected. `FE_HOST=1 ./up.sh` does the setup:
  - creates a self-signed certificate for the LAN host, or reuses the one it made before, so a phone that accepted it stays accepted;
  - serves the API on `https://<machine>.local:5443` through a compose override;
  - allows the page's LAN origin;
  - points `API_URL` at the HTTPS address;
  - prints the API URL to open once on the phone to accept the certificate.
- **Config and scripts:** `appsettings.Development.json` allows the local dev and preview origins. `up.sh`, `test-e2e.sh`, the CI e2e job and both compose files pass the address and origins they need. `FE/README.md` and `FE/DEVICE-CHECKLIST.md` describe the new setup.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `browser-client`: adds a requirement that the client reaches the ledger at an address configured when its server starts, and that a missing or malformed address is refused at startup instead of producing a client that cannot load.
- `api-surface`: adds a requirement that the HTTP interface accepts cross-origin browser calls only from configured origins, including on error responses, and that it rejects invalid origin configuration at startup. It also adds a requirement that the interface can be served over HTTPS from configuration alone, with plain HTTP still answered and not redirected.

## Impact

- **FE code:** `vite.config.ts` (proxy removed; a small plugin serves the runtime config), `index.html` (loads the config before the app), a new `src/api/` module that reads the address, `client.ts` and `writes.ts`, plus their unit tests. `playwright.config.ts` and the `e2e/capture.spec.ts` header comment change too.
- **API code:** `Program.cs` gains a CORS policy bound from configuration, with startup validation. `appsettings.Development.json` gains the local origins. New integration tests go in `BE/tests/Expenses.Integration.Tests/Api/`.
- **Ops:** `docker-compose.yml`, `docker-compose.e2e.yml`, a new `docker-compose.lan.yml` override, `up.sh`, `test-e2e.sh`, `.github/workflows/ci.yml`, and `.gitignore` (the generated `.certs/`).
- **Not affected:** the MCP host, which is not called by browsers, and the Avalonia desktop client, which already takes its address from configuration and is not subject to CORS.
- **Phone testing on a LAN keeps working.** Without the HTTPS work above, removing the proxy would break it: the phone resolves `localhost` to itself, and an http call from the https page is blocked as mixed content. The price is one extra step on each phone: open the printed API URL once and accept the certificate warning, just as the page's own certificate is accepted today.
- **No new dependencies.** CORS and HTTPS are built into ASP.NET Core, the Vite plugin is a few lines inside `vite.config.ts`, and certificates are generated with the `openssl` that ships with Git Bash.
