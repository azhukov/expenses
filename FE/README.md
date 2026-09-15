# Expenses — browser client

The human front door onto the ledger: React, Vite and TypeScript, used primarily one-handed on a
phone at the point of purchase. Home is a **launcher**, not a dashboard — a large, always-reachable
capture control with the month's total and a few recent purchases beneath it.

One screen exists today. Uploading a captured image, reviewing what was extracted, and confirming
it into a purchase are a later change; `/capture` is a placeholder that names the file it was
handed and does nothing else.

## Running it

The client expects the API on `http://localhost:5082`. Bring it up from the repository root:

```bash
docker compose up -d --build        # or: docker compose up -d postgres, then dotnet run in BE/
```

Then, in `FE/`:

```bash
npm install
npm run dev                         # http://localhost:5173
FE_HTTPS=1 npm run dev -- --host    # reachable from a phone on the same network, over HTTPS
```

A phone needs HTTPS: a LAN address is not a secure context, and the camera is only offered to one.
`FE_HTTPS=1` serves a self-signed certificate, so the phone warns once before it opens the page.
Reach it at `https://<machine-name>.local:5173` (mDNS, which iOS and recent Android resolve) or at
the Network address Vite prints. On Windows the network has to be a Private one, or the firewall
drops the connection. In PowerShell, set the variable with `$env:FE_HTTPS=1` first.

| | |
| --- | --- |
| `npm run dev` | Vite dev server, with the `/api` proxy |
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

## It is not production-deployable yet

The client calls the API through same-origin `/api/...` paths, and `vite.config.ts` proxies those
to `:5082` in development. The API has no CORS configuration, and this change adds none: the proxy
is what makes development same-origin, and it keeps the request code identical to what a
same-origin deployment would need (D9).

Deciding how the built bundle is served in production — static files behind the API, a separate
nginx service, or a separate origin with CORS — is a real decision with its own trade-offs, and it
is deliberately not made here. Until it is, `npm run build` produces a bundle nothing serves.

There is no authentication, because the API has none. The client assumes a single trusted user on a
private network.

## Layout

```
src/
  api/          typed fetch over /api, the read functions, and the query hooks
  capture/      the camera-backed capture control
  components/   the header, the review banner and the recent list
  format/       amounts and dates, en-GB and EUR in one place
  labels/       merchant and category display names, resolved against the dictionaries
  month/        the month boundary and the figures derived from one month's purchases
  routes/       home, and the placeholder capture screen
```

## Two things worth knowing before changing it

**The capture control must stay a plain `<label>` around a file input.** iOS Safari opens the
camera only for the interaction that asked for it. Navigating first and triggering the input on the
destination screen spends the gesture, and iOS then does nothing at all — no error, no camera.
Android is more permissive, which is what makes this a bug that passes every test but the one
device that matters. There is a test asserting no navigation is dispatched before the input is
activated; if it fails, that is why (D3).

**Captured bytes are never resized or re-encoded.** Fiscal QR codes on thermal paper are dense and
marginal, and a decode miss is by design indistinguishable from a receipt carrying no code — so
downscaling degrades extraction invisibly. The `File` is handed on untouched (D4).

`DEVICE-CHECKLIST.md` covers what jsdom cannot observe: the camera opening, the safe-area insets,
and the collapsing-toolbar viewport.
