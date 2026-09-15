## Context

- The client builds every URL from `const BASE = '/api'`. The constant appears twice, in [FE/src/api/client.ts](../../../FE/src/api/client.ts) (`read`, `send`) and [FE/src/api/writes.ts](../../../FE/src/api/writes.ts) (the multipart upload). Nothing else calls `fetch`.
- [FE/vite.config.ts](../../../FE/vite.config.ts) sets up one proxy object for both `server` and `preview`. It strips `/api` and forwards to `E2E_API_URL ?? http://localhost:5082`. The same file also holds the vitest config, so vitest loads it.
- The e2e suite runs against `vite preview` over a bundle that CI builds *before* the stack starts ([.github/workflows/ci.yml](../../../.github/workflows/ci.yml) "Build the client"). A setting fixed at build time would have to be known in that earlier step.
- [BE/Expenses.Api/Program.cs](../../../BE/Expenses.Api/Program.cs) has no CORS. Errors are written by `UseExpensesErrors`, an `UseExceptionHandler` that clears the response before writing `ErrorResponse`. Model-binding failures come back as `BadRequestObjectResult` from MVC.
- The API uses `GET`, `POST`, `PUT` and `DELETE`, and its requests carry `accept`, `content-type` and multipart bodies. Nothing uses cookies or credentials, because there is no authentication.
- Integration tests host the real API through `ExpensesApi` (a `WebApplicationFactory`) and take configuration as key/value pairs, so CORS settings can be tested per test.
- The API listens on HTTP only (`ASPNETCORE_HTTP_PORTS: 8080`, published as `5082`) and has no `UseHttpsRedirection`. The FE already offers HTTPS for phones through `@vitejs/plugin-basic-ssl`, whose self-signed certificate the phone accepts once. `FE_HOST=1 ./up.sh` prints `https://<hostname>.local:5173` for the phone.

## Goals / Non-Goals

**Goals:**

- One built client can be pointed at any API by setting an environment variable before the server that serves it starts.
- A misconfiguration on either side fails when a process starts, not on the first request in a browser.
- An `ErrorResponse` stays readable when the client and API are on different origins.
- Phone testing over the LAN (`FE_HOST=1 ./up.sh`) keeps working with the proxy gone.

**Non-Goals:**

- Choosing a production host for the built bundle. This change defines what that host has to provide (see D2), but it does not add one.
- A certificate the phone trusts without a warning (a local CA such as mkcert), or HTTPS for any stack other than the LAN one (see D6).
- Authentication, cookies or credentialed CORS.
- Changing the MCP host or the desktop client.

## Decisions

### D1 — The FE reads `API_URL` when its server starts and hands it to the page at runtime

A small Vite plugin, defined in `vite.config.ts`, does the following in `configureServer` and `configurePreviewServer`:

1. It reads `API_URL` through `loadEnv(mode, cwd, '')`. A shell variable wins, and a git-ignored `FE/.env.local` also works.
2. It validates and normalises the value: it must be an absolute `http:`/`https:` URL, and any trailing slash is trimmed. If the value fails, it throws an error naming `API_URL` and the reason, and the server does not start.
3. It adds a middleware that serves `GET /config.js` as `window.__EXPENSES_CONFIG__ = {"apiUrl": "…"}`, with `Cache-Control: no-store`.

`index.html` loads `<script src="/config.js"></script>` before the module entry. The client reads the global through a single accessor in `src/api/`, which `client.ts` and `writes.ts` both call instead of `BASE`.

**Why:** the user asked for the URL to come from an environment variable at startup. A runtime script is the only mechanism that holds this for `vite preview` too. `preview` serves a bundle that was already built, so `import.meta.env.VITE_API_URL` would be fixed when CI ran `npm run build` and would ignore the environment the preview server starts with. The script also defines the contract any future production host must meet: serve `/config.js` from its own environment.

**Alternatives rejected:**

- *`VITE_API_URL` via `import.meta.env`.* It is simpler, but it is fixed at build time. It fails the "different address needs no rebuild" scenario, and CI would need the address in the build step.
- *Fetching `config.json` from `main.tsx` before rendering.* This adds an async boot path, plus a loading and failure state before React mounts. A classic blocking `<script>` gives the same runtime value with no new code path.

**Validation lives in a pure function.** `parseLedgerAddress(value): string` either returns the normalised URL or throws with the message. It sits in its own module with a colocated vitest test, and `vite.config.ts` imports it. The plugin itself is just wiring, and the e2e run covers that wiring.

**The plugin does not run under vitest.** Vitest starts a Vite server of its own, so `configureServer` would demand `API_URL` for every unit-test run. The plugin is left out when `process.env.VITEST` is set. Unit tests set `window.__EXPENSES_CONFIG__` in `src/test/setup.ts` to a fixed test address instead.

**If the global is absent at request time** (for example, a host that serves the bundle without `/config.js`), the accessor throws `LedgerError('The ledger address is not configured.')`. The existing error display then shows that message, so the client has no second error vocabulary.

### D2 — The proxy is removed, not kept as a fallback

`server.proxy` and `preview.proxy` are deleted, along with `E2E_API_URL`. Every environment then uses the same path: the browser calls the API directly, and CORS decides whether the call is allowed.

**Why:** the user chose this. It also means dev and e2e exercise the same cross-origin behaviour a separately hosted client will depend on. With a proxy fallback, the CORS headers on error responses (D4) would never be exercised outside the integration tests.

### D3 — CORS is one named policy bound from `Cors:AllowedOrigins`

`Program.cs` binds `Cors:AllowedOrigins` as a `string[]`, following the project's preference for plain primitives over an options wrapper where one list will do. It validates each entry when the host starts:

- `Uri.TryCreate(entry, Absolute)`
- scheme is `http` or `https`
- path is empty or `/`, with no query and no fragment
- not `*`

Validation is fail-fast at startup, like the provisioning assertion. The first invalid entry throws an exception whose message names the entry. The API then registers one policy with:

- `WithOrigins(normalised entries)`, with trailing slashes trimmed
- `WithMethods("GET", "POST", "PUT", "DELETE")`
- `WithHeaders("accept", "content-type")`
- `SetPreflightMaxAge(10 minutes)`
- no `AllowCredentials`

An empty or absent list registers the same policy with no origins, so every cross-origin request is refused.

**Why exact origins and explicit methods and headers:** the spec requires exact matching, and there is no authentication to lean on, so the allowed surface should be what the client uses and no more. `AllowAnyHeader` would save one line and quietly widen the surface.

**Alternative rejected:** `AllowAnyOrigin` in Development. It is convenient, but then Development and the e2e stack would test a policy no other environment runs.

### D4 — `UseCors` runs before `UseExpensesErrors`

The API calls `app.UseCors(policy)` as the first middleware, before the exception handler. For a normal request, the CORS middleware adds its headers through a response-starting callback, and `UseExceptionHandler` clearing the response does not remove that callback. As a result, a 404, 409 or 500 written by the error handler, and a 400 written by MVC, all carry the headers.

**What the tests pin, and what they don't:** the "error response is readable cross-origin" scenarios are integration tests that throw through the real handler, so any change that strips the headers from an error response fails the build. During implementation, swapping the two middleware calls turned out to make no difference, because the headers are added through the response-starting callback either way. The order is therefore a convention (judge the origin before anything else answers), not something a test can hold. The behaviour it protects is pinned.

### D5 — Where the origins and address are set

| Place | `API_URL` (FE) | `Cors:AllowedOrigins` (API) |
| --- | --- | --- |
| `appsettings.Development.json` | — | `http://localhost:5173`, `https://localhost:5173`, `http://localhost:4173` |
| `appsettings.json` | — | none (fail closed) |
| `docker-compose.yml`, `docker-compose.e2e.yml` | — | inherits Development; a comment points at the `Cors__AllowedOrigins__N` override |
| `up.sh` | exports `http://localhost:5082` unless already set | — |
| `FE_HOST=1 ./up.sh` | exports `https://$LAN_HOST:5443` unless already set | `docker-compose.lan.yml` adds `https://$LAN_HOST:5173` as `Cors__AllowedOrigins__3` |
| `test-e2e.sh`, CI e2e "Run the suite" | `http://localhost:5082` | — |
| `playwright.config.ts` | fails early with a clear message if `API_URL` is unset and it is starting its own preview server | — |

Both API containers already run as `Development`. The task list therefore checks that the image contains `appsettings.Development.json` before relying on it, rather than duplicating the origins into the compose files.

The LAN origin is added at index `3` because configuration merges arrays by index: indexes 0–2 come from `appsettings.Development.json`. Both files carry a comment saying so, because adding a fourth Development origin would silently overwrite the LAN one.

### D6 — HTTPS for the API on the LAN stack: a self-signed certificate, a compose override, no host code

**The host.** HTTPS is enabled through configuration alone, using Kestrel's own keys: `ASPNETCORE_HTTPS_PORTS`, `Kestrel__Certificates__Default__Path` and `Kestrel__Certificates__Default__KeyPath`, with the certificate and key as PEM files. `Program.cs` gains nothing, and in particular no `UseHttpsRedirection`: HTTP on 8080 must keep answering for e2e, the desktop client and the healthcheck. The api-surface HTTPS requirement pins exactly that. An integration test starts the real host on Kestrel (`WebApplicationFactory`'s Kestrel mode in .NET 10) with a certificate generated in memory and written to a temp directory. It then asserts HTTPS answers with that certificate, HTTP answers without a redirect, CORS headers appear over HTTPS, and a missing certificate path stops the host. The test exists so that someone adding HTTPS redirection or an HTTPS-only binding later breaks the build instead of the phone flow.

**The stack.** A new `docker-compose.lan.yml` override, used by `up.sh` only when `FE_HOST` is set, does four things:

- mounts `./.certs` read-only;
- sets `ASPNETCORE_HTTPS_PORTS: 8443` alongside the existing HTTP port;
- sets the two certificate paths;
- publishes `5443:8443` and adds `Cors__AllowedOrigins__3: https://${LAN_HOST}:5173`.

`up.sh` passes it as a second `-f`. The base `docker-compose.yml` and `docker-compose.e2e.yml` stay HTTP-only.

**The certificate.** Before starting compose, `up.sh` does the following:

1. Resolves `LAN_HOST`: the variable if set (a name or an IPv4 address), else `$(hostname).local`, lower-cased.
2. If `.certs/lan.pem` is missing, or its SANs don't include `LAN_HOST`, generates one with `openssl req -x509`. The certificate uses an RSA 2048 key and lists `LAN_HOST` in the SAN as `DNS:` or `IP:` as appropriate, plus `DNS:localhost`. It is marked for server authentication (`extendedKeyUsage=serverAuth`) and is valid for 397 days.
3. Otherwise reuses the existing certificate.
4. Exports `API_URL=https://$LAN_HOST:5443` for Vite, and prints the API URL to open once on each device, `https://$LAN_HOST:5443/openapi/v1.json`, next to the page URL it already prints.

`.certs/` is added to `.gitignore`.

**Why these certificate parameters:** iOS rejects any TLS server certificate that lacks a SAN or the serverAuth usage, has a key shorter than 2048 bits, or is valid for more than 825 days, and it does so even after the user accepts a warning. 397 days stays under every browser's limit.

**Why the certificate is reused:** an exception the phone accepted is tied to that certificate. Regenerating on every run would make the phone warn again every time.

**Why an override, not a change to the base stack:** the plain developer stack and the e2e stack have no use for HTTPS. Keeping it out of them means a missing `.certs/` can never stop those stacks from starting.

**Alternatives rejected:**

- *A local CA via mkcert.* The phone shows no warning and iOS is more reliable, but it needs an extra tool plus a root CA installed on every phone. The user chose accept-once.
- *Sharing one certificate with the Vite server instead of `plugin-basic-ssl`.* It might save one acceptance on some browsers, but browsers disagree about whether an exception covers other ports on the same host. It would also mean changing how Vite's HTTPS works for no guaranteed gain, so the FE's certificate stays as it is.
- *`dotnet dev-certs https`.* That certificate only names `localhost`, so a phone reaching `<machine>.local` would see a name mismatch on top of the self-signed warning, and exporting it into the container needs extra steps on every platform.

**An https page calling `http://localhost` is not mixed content.** Browsers treat `localhost` as potentially trustworthy. `FE_HTTPS=1` alone, on localhost, therefore keeps `API_URL=http://localhost:5082` and needs no API certificate.

## Risks / Trade-offs

- **[A certificate the device has not accepted looks like a network failure]** → A `fetch` to an untrusted certificate fails with no warning page, so the client shows "The ledger could not be reached." `up.sh` prints the API URL to open next to the page URL. `FE/README.md` and `DEVICE-CHECKLIST.md` list "open the API URL and accept" as the first thing to try.
- **[iOS may forget the exception]** → Safari can drop an accepted exception, for example after the browser restarts. The remedy is the same: open the API URL again. The certificate is reused, so this is a tap-through, not a new certificate.
- **[Desktop browsers on the LAN stack also need to accept once]** → With `FE_HOST` set, `API_URL` is the LAN HTTPS address for every page Vite serves, including the `https://localhost:5173` tab `up.sh` opens. That tab needs the same one-time acceptance. `up.sh` says so.
- **[`.local` names are not resolved everywhere]** → Some Android versions and networks do not resolve mDNS. Setting `LAN_HOST` to the machine's IPv4 address covers this: the certificate is regenerated with an `IP:` SAN, and the phone opens the IP-based URLs instead.
- **[LAN origin index collides with Development origins]** → See D5. There is a comment in both files, and the manual phone check in the tasks proves the origin is allowed.
- **[Every dev run needs `API_URL`]** → The failure is immediate and names the variable. `up.sh` sets it, and the README shows the one-liner and the `.env.local` option.
- **[`localhost` vs `127.0.0.1` origins differ]** → The browser treats them as different origins. The README states that the origin the page is opened on must be listed. The configured defaults are the `localhost` forms the scripts open.
- **[`/config.js` is the contract for any future host]** → A host that serves `dist/` without it gets the "not configured" message on every request rather than a silent failure. This is recorded in the README where "It is not production-deployable yet" is rewritten.
- **[A blocking script before the app]** → The script is one small, uncached local request. It is acceptable for a single-user app on a private network.

## Migration Plan

1. Land the API side first: CORS with the Development origins, plus the HTTPS test. With the proxy still in place, the client is unaffected, because proxied requests are not cross-origin.
2. Land the FE side, scripts, the LAN override and CI together. That removes the proxy, adds `API_URL`, and wires the scripts. The phone flow therefore never has a commit in which it is broken.
3. Developers set `API_URL`, or use `./up.sh`, from then on. Phone testers accept the API certificate once per device.

Rollback: revert the FE commit to bring the proxy back. The API's CORS policy is harmless without a cross-origin caller, so it can stay, and so can the LAN override and `.certs/`.
