## 1. API: CORS from configuration

- [x] 1.0 Write failing integration tests in `BE/tests/Expenses.Integration.Tests/Api/` (a new `CorsTests.cs`, hosting `ExpensesApi` with `Cors:AllowedOrigins:N` settings) for the api-surface scenarios "An allowed origin reads the ledger", "An unlisted origin is not allowed", "Origins match exactly", "Nothing configured allows nothing", "Credentials are not allowed" and "Requests without an origin are unaffected". Assert on `Access-Control-Allow-Origin` and `Access-Control-Allow-Credentials` for `GET /categories`.
- [x] 1.1 Write failing tests for "A preflight for a write is answered" (`OPTIONS /purchases` with `Access-Control-Request-Method: POST` and `Access-Control-Request-Headers: content-type`) and "Methods beyond POST are allowed" (`PUT` and `DELETE` preflights).
- [x] 1.2 Write failing tests for "An error response is readable cross-origin". Cover a 400 from model binding (an unreadable body), a 404 from the error handler (an unknown purchase), and a 500 from the error handler (with a service replaced to throw). Each must carry the `ErrorResponse` shape and allow the origin.
- [x] 1.3 Write failing tests for "A wildcard origin stops the host" and "An origin with a path stops the host". Starting `ExpensesApi` with `*`, or with `http://localhost:5173/app`, throws, and the message contains the offending value.
- [x] 1.4 In `Program.cs`, bind `Cors:AllowedOrigins` as a `string[]` and validate each entry at startup (D3). Register the named policy with exact origins, `GET/POST/PUT/DELETE`, `accept`/`content-type` headers, a 10-minute preflight max-age and no credentials. Call `app.UseCors` before `app.UseExpensesErrors()` (D4). 1.0–1.3 go green.
- [x] 1.5 Add `http://localhost:5173`, `https://localhost:5173` and `http://localhost:4173` to `Cors:AllowedOrigins` in `appsettings.Development.json`. Leave `appsettings.json` without origins. Config only, no test.
- [x] 1.6 Confirm the published API image contains `appsettings.Development.json`: `docker compose -f docker-compose.e2e.yml up -d --build --wait`, then a `curl -H "Origin: http://localhost:4173"` request to `/categories` shows the allow header. In both compose files, add a comment pointing at the `Cors__AllowedOrigins__N` override. Verification and config, no test.
- [x] 1.7 Write integration tests (a new `HttpsTests.cs`) for the api-surface requirement "The HTTP interface can be served over HTTPS" (D6). Start the real host on Kestrel (`WebApplicationFactory` Kestrel mode) with a certificate generated in memory by `CertificateRequest` and written as PEM to a temp directory, then cover:
  - "A configured certificate serves HTTPS": the response body matches HTTP, and the server certificate's thumbprint matches the generated one.
  - "HTTP is not redirected".
  - "Cross-origin headers over HTTPS", with an `https://phone.test:5173` origin configured.
  - "No certificate means HTTP only".
  - "An unreadable certificate stops the host".

  Unlike the other test tasks, these are expected to pass on their first run, because Kestrel already provides the behaviour through configuration and no host code is added. Record that in the test file's summary: the tests exist to fail if redirection or an HTTPS-only binding is added later. Run each once with `app.UseHttpsRedirection()` temporarily added, to see it fail, before trusting it.
- [x] 1.8 Record the new scenarios in `BE/tests/SCENARIO-COVERAGE.md`, and run the `be-editorconfig-fix` checks until the BE build is clean. Documentation and tooling, no test.

## 2. FE: the ledger address

- [x] 2.0 Write failing colocated vitest cases for `parseLedgerAddress` (D1) from the browser-client scenarios:
  - "A missing address stops the server": `undefined` and empty string throw a message naming `API_URL`.
  - "A malformed address stops the server": a relative path, `ftp://…` and `not a url` each throw, naming `API_URL` and the reason.
  - "A trailing slash does not change the requests": `http://ledger.test:9000/` returns `http://ledger.test:9000`.
  - A path-bearing address such as `https://host/ledger/` returns `https://host/ledger`.
- [x] 2.1 Implement `parseLedgerAddress` as a pure module in a location that satisfies `structure.config.json` and that `vite.config.ts` can import. Extend `tsconfig.node.json`'s `include` if needed. 2.0 goes green.
- [x] 2.2 Update `client.test.ts`, `writes.test.ts` and `reads.test.ts`: requested URLs are built from the configured address, not `/api`, per "Requests go to the configured address" and "Uploads go to the configured address". Add a case where the configured global is absent, so a read rejects with `LedgerError` "The ledger address is not configured." Set `window.__EXPENSES_CONFIG__` to a fixed test address in `src/test/setup.ts`. The updated tests fail.
- [x] 2.3 Add the address accessor in `src/api/`, and replace `BASE` in `client.ts` and `writes.ts` with it. Add the `window.__EXPENSES_CONFIG__` global type declaration. 2.2 goes green.
- [x] 2.4 Write a failing test for "Ledger errors are still shown cross-origin" at the client level: an `ErrorResponse` returned from the configured cross-origin address surfaces its message through `LedgerError`. This guards against the accessor or request options, such as a `mode` or `credentials` setting, changing how the response is read.
- [x] 2.5 In `vite.config.ts`, add the plugin (D1). In `configureServer` and `configurePreviewServer`, call `loadEnv(mode, cwd, '')` and then `parseLedgerAddress(env.API_URL)`. Serve `GET /config.js` with `no-store`. Skip the plugin when `process.env.VITEST` is set. Delete `server.proxy`, `preview.proxy` and `E2E_API_URL`, and rewrite the D9 comment. Load `<script src="/config.js"></script>` before the module entry in `index.html`. This is wiring over the tested function, and the e2e run in 3.3 covers it.
- [x] 2.6 Manually check the startup scenarios: `npm run dev` and `npm run preview` with `API_URL` unset, and with `API_URL=nope`, each exit with the message naming `API_URL`. `npm test` still runs without `API_URL`.
- [x] 2.7 Run the `fe-style-fix` checks (`npm run style`) until clean. Tooling, no test.

## 3. Scripts, e2e and CI

- [x] 3.1 In `playwright.config.ts`, when starting its own preview server, fail with a clear message if `API_URL` is unset, and pass the environment through to `npm run preview`. Update the header comments in `playwright.config.ts`, `e2e/capture.spec.ts` and `docker-compose.e2e.yml` that describe the proxy. Config and comments, no test.
- [x] 3.2 `up.sh`: export `API_URL=http://localhost:5082` unless already set, when `FE_HOST` is not set. The `FE_HOST` path is section 4. `test-e2e.sh`: export `API_URL=http://localhost:5082` for the suite. Scripts, no test.
- [x] 3.3 In the CI e2e job, set `API_URL: http://localhost:5082` on "Run the suite". Run `./test-e2e.sh` locally and confirm the capture flow passes with the browser calling the API cross-origin (`http://localhost:4173` to `http://localhost:5082`). This is the end-to-end proof of "A different address needs no rebuild" and of the CORS wiring.

## 4. LAN HTTPS for phone testing

Everything in this section is scripts, compose and config, so none of it gets its own test (task 1.7 covers the host's HTTPS behaviour). 4.5 is the check that proves the section works.

- [x] 4.1 Add `.certs/` to the root `.gitignore`.
- [x] 4.2 Add `docker-compose.lan.yml` as an override of the `api` service (D6). It mounts `./.certs:/certs:ro`, sets `ASPNETCORE_HTTPS_PORTS: 8443`, `Kestrel__Certificates__Default__Path: /certs/lan.pem` and `Kestrel__Certificates__Default__KeyPath: /certs/lan-key.pem`, publishes `5443:8443`, and sets `Cors__AllowedOrigins__3: https://${LAN_HOST:?LAN_HOST is set by up.sh}:5173`. The header comment explains the file's purpose, that `up.sh` is its only caller, and the index-3 rule (D5). Add the matching index comment next to the origins in `appsettings.Development.json`.
- [x] 4.3 In `up.sh`, when `FE_HOST` is set, before `docker compose`:
  - resolve `LAN_HOST` (the variable if set, else `$(hostname).local`, lower-cased);
  - generate `.certs/lan.pem` and `.certs/lan-key.pem` with `openssl req -x509` if they are missing or their SANs lack `LAN_HOST`: RSA 2048, SAN `DNS:`/`IP:` for `LAN_HOST` plus `DNS:localhost`, `extendedKeyUsage=serverAuth`, 397 days;
  - pass `-f docker-compose.lan.yml` to `docker compose`;
  - export `API_URL=https://$LAN_HOST:5443` unless already set.
  If `openssl` is missing, exit with a message rather than starting a stack the phone cannot use.
- [x] 4.4 In the `up.sh` output, when `FE_HOST` is set, print the API URL `https://$LAN_HOST:5443/openapi/v1.json` next to the page URL. Include one line telling the user to open it once on each device, desktop tab included, and accept the warning, because a certificate that hasn't been accepted shows as "The ledger could not be reached". Replace "on this network" wording that assumes `.local` with the resolved `LAN_HOST`.
- [ ] 4.5 Manual check on a real phone. Run `FE_HOST=1 ./up.sh`, accept the API certificate on the phone, open the page, and confirm the purchase list loads and a capture uploads. Run it again and confirm the certificate is reused (the same thumbprint, and the phone does not warn again). Repeat once with `LAN_HOST=<IPv4>` to confirm the `IP:` SAN path. Also confirm plain `./up.sh` starts without `.certs/` and serves no 5443.

## 5. Documentation

- [x] 5.1 `FE/README.md`: replace the `/api` proxy row and the "It is not production-deployable yet" section. Describe `API_URL` (shell variable or `FE/.env.local`), the `/config.js` contract any host of `dist/` must meet, and the requirement that the page's exact origin be listed in the API's `Cors:AllowedOrigins`, including the `localhost` vs `127.0.0.1` caveat. In the phone section, describe `FE_HOST=1 ./up.sh`, the two one-time certificate acceptances (page and API), `LAN_HOST` for networks without mDNS, and the troubleshooting line: "could not be reached" usually means the API certificate has not been accepted. Documentation, no test.
- [x] 5.2 `BE/README.md`: add `Cors:AllowedOrigins` to the configuration table, with its validation rules and the fail-closed default. Add a note that HTTPS is enabled by Kestrel's certificate settings alone and HTTP is never redirected. Documentation, no test.
- [x] 5.3 `FE/DEVICE-CHECKLIST.md`: add a setup step before the checks. Open the API URL `up.sh` prints and accept the certificate, then open the page. Add the "could not be reached" → re-accept remedy, including that iOS may forget the exception. Documentation, no test.
