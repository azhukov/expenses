# Expenses — browser client

The human front door onto the ledger: React, Vite and TypeScript, used primarily one-handed on a
phone at the point of purchase. Home is a **launcher**, not a dashboard — a large, always-reachable
capture control with the month's total and a few recent purchases beneath it.

The capture action leads to `/capture`, which is **QR first**. It scans the rear camera for the
receipt's fiscal code and sends the first code it reads on its own. If the ledger fetches the
invoice for that code, the invoice is the whole capture and no photograph is taken. Otherwise, and
whenever there is no live camera, the screen offers a photograph, which is uploaded for
extraction. The capture is then reviewed and confirmed into a purchase.

## Running it

`./up.sh` from the repository root does all of this. By hand, bring the API up first:

```bash
docker compose up -d --build        # or: docker compose up -d postgres, then dotnet run in BE/
```

Then, in `FE/`:

```bash
npm install
API_URL=http://localhost:5082 npm run dev    # http://localhost:5173
```

The client calls the API directly, at **`API_URL`**. The dev and preview servers read it when they
start, from the shell or from a git-ignored `FE/.env.local` (`API_URL=http://localhost:5082`), and
refuse to start without a usable one. There is no proxy. In PowerShell, set it with
`$env:API_URL="http://localhost:5082"` first.

The API only answers pages from origins it lists in `Cors:AllowedOrigins`. In Development those are
`http://localhost:5173`, `https://localhost:5173` and `http://localhost:4173`. The match is exact:
a page opened at `http://127.0.0.1:5173` is a different origin, and every call from it is refused.

### From a phone

```bash
FE_HOST=1 ./up.sh                   # from the repository root
```

A phone needs HTTPS: a LAN address is not a secure context, and the camera is only offered to one.
The page's calls to the API then have to be HTTPS as well, or the browser blocks them as mixed
content. `FE_HOST=1 ./up.sh` does the setup:

- serves the page at `https://<LAN_HOST>:5173`;
- serves the API at `https://<LAN_HOST>:5443`, with a self-signed certificate it makes once in
  `.certs/` and reuses on later runs;
- allows that page's origin;
- prints both URLs.

`LAN_HOST` is `<machine-name>.local` (mDNS, which iOS and recent Android resolve). Where the phone
cannot resolve it, rerun with `LAN_HOST=<the machine's IPv4 address>`.

On each device, accept two certificates, once each:

1. Open the **API URL** `up.sh` prints (`https://<LAN_HOST>:5443/openapi/v1.json`) and accept the warning.
2. Open the page and accept its warning.

**"The ledger could not be reached" usually means step 1 was skipped.** A request to an
unaccepted certificate fails silently, with no warning to click through. iOS can also forget an
accepted exception, so the remedy is the same: open the API URL again. On Windows the network has
to be a Private one and inbound connections to 5173 and 5443 allowed, or the firewall drops them.

| | |
| --- | --- |
| `npm run dev` | Vite dev server; needs `API_URL` |
| `npm test` | Vitest, once |
| `npm run test:watch` | Vitest, watching |
| `npm run test:e2e` | Playwright, against a real API and database — see below |
| `npm run lint` | ESLint |
| `npm run style` | The whole style gate: Prettier, ESLint, tsc, structure |
| `npm run structure` | The `src/` shape rules alone (`structure.config.json`) |
| `npm run build` | Type-check and bundle into `dist/` |

## The three test suites

`npm test` is the fast one and the one to run while working: jsdom, `fetch` mocked, colocated with
the code it covers. It never starts a server.

`npm run test:e2e` is the opposite — a real browser, the built client, the real HTTP host and a
real PostgreSQL, with nothing stubbed. It needs that stack running, so the way to invoke it is
[`../test-e2e.sh`](../test-e2e.sh) from the repository root, which brings the stack up on an empty
database, builds the client, runs the suite and tears everything down. `npm run test:e2e` on its
own assumes you have already done that.

The third is the backend's own, in `BE/` — see [BE/README.md](../BE/README.md). All three, plus
both style gates, are what `.github/workflows/ci.yml` runs on a pull request.

## Where the rules live

Four tools, split by what each is good at: Prettier owns layout (`.prettierrc.json`), ESLint owns
what a formatter cannot decide (`eslint.config.js`), tsc owns the types, and
[`scripts/check-structure.mjs`](scripts/check-structure.mjs) owns the shape of `src/` — file
naming, colocation, and the layer map in [`structure.config.json`](structure.config.json) that
ESLint also reads to police the import graph. `npm run style` is all four.

## Serving the built client

`npm run build` puts a bundle in `dist/` that does not contain the API address. `index.html` loads
`/config.js` before the app, and whatever serves `dist/` must answer that path with:

```js
window.__EXPENSES_CONFIG__ = {"apiUrl":"https://ledger.example"}
```

The script is written from the host's own environment when it starts, with `no-store`, so one build
can be pointed at any API. `vite preview` does exactly this from `API_URL`, and the e2e suite relies
on it. A host that serves `dist/` without the script gets a client whose every request fails with
"The ledger address is not configured." The build prints a notice that `/config.js` is not bundled;
that is intended.

The page's origin must also be in the API's `Cors:AllowedOrigins` (see
[BE/README.md](../BE/README.md)).

### The deployed client: `vite preview` in a container

The client is deployed to Railway (<https://expenses-frontend-production-2e52.up.railway.app>) from
[Dockerfile](Dockerfile). The image runs `npm ci` and `npm run build`, then runs `vite preview` on
`$PORT`. It takes two settings from its environment at startup, never at build time:

- `API_URL` is the API's **public** address, `https://expenses-api-production-4d79.up.railway.app`.
  The browser makes the calls, so a private `*.railway.internal` name cannot work.
- `PREVIEW_ALLOWED_HOSTS` is a comma-separated list of the host names the server answers besides
  `localhost`. Vite answers any other `Host` header with a 403. On Railway, set it to
  `${{RAILWAY_PUBLIC_DOMAIN}},healthcheck.railway.app`, because the healthcheck is sent under that
  second name. An entry with a scheme, port or path stops the server.

`npm run test:preview-hosts` builds, then checks that a listed host is served (with `/config.js`)
and an unlisted one is refused. Railway service settings are in
[BE/README.md](../BE/README.md#railway).

There is no authentication, because the API has none. The deployed API has a public URL, so anyone
who knows it can call it. CORS only limits which web pages can read its responses.

## Layout

```
src/
  api/          typed fetch over /api, the read functions, and the query hooks
  capture/      Home's capture link, the QR scanner module, and the review rules
  components/   the header, the review banner and the recent list
  format/       amounts and dates, en-GB and EUR in one place
  labels/       merchant and category display names, resolved against the dictionaries
  month/        the month boundary and the figures derived from one month's purchases
  routes/       home, the capture screen (scan, then photograph) and its review
```

## Three things worth knowing before changing it

**The photograph control must stay a plain `<label>` around a file input.** It lives on the capture
screen. iOS Safari opens the camera only for the interaction that asked for it. Triggering the input
programmatically, or putting anything between the tap and the input, spends the gesture, and iOS
then does nothing at all: no error, no camera. Android is more permissive, which is what makes this
a bug that passes every test but the one device that matters (D3).

**The QR library is behind `src/capture/scanner.ts` and nowhere else.** `qr-scanner` uses the
browser's native `BarcodeDetector` where there is one (Android Chrome) and its own worker elsewhere
(iOS Safari). An unavailable camera, a refused permission and a missing API all come back as "no
live camera", and the screen then offers the photograph with no error. Swapping the library touches
that one module (D40).

**Captured bytes are never resized or re-encoded.** Fiscal QR codes on thermal paper are dense and
marginal, and a decode miss is by design indistinguishable from a receipt carrying no code — so
downscaling degrades extraction invisibly. The `File` is handed on untouched (D4).

`DEVICE-CHECKLIST.md` covers what jsdom cannot observe: the camera opening, the safe-area insets,
and the collapsing-toolbar viewport.
