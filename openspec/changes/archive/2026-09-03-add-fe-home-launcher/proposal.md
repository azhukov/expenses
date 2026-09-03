## Why

The ledger has two front doors — HTTP and MCP — and no human one. `FE/` holds a single `.gitkeep`. Everything the backend was built for happens through Swagger or an assistant, which means the one motion the system is designed around cannot actually be performed: standing at a till, on a phone, photographing a paper receipt.

That motion is not incidental. The `add-receipt-first-capture` change deliberately removed the ability to attach a receipt to a purchase created by hand, on the grounds that typing the purchase out first is "exactly the data entry the receipt image was meant to replace". A browser client is what makes that argument true rather than aspirational, and it has to be a client that works one-handed on a phone.

This change establishes the client — its build, its shell, its data access, its mobile behaviour — and lands exactly one screen on it: the home page. Home is a **launcher**, not a dashboard: a large, always-reachable capture affordance with a short list of recent purchases beneath it, so the app answers "log this receipt" first and "what did I spend" second.

## What Changes

- **New front-end application** in `FE/`: React (latest stable), Vite, TypeScript, built and run as a single-page application. No front-end code exists today, so this introduces the toolchain, the project layout, the linting and test setup, and the application shell.
- **Client-side routing** with a single route (`/`) registered from the outset, so that the capture and review screens this change does not build can be added without restructuring.
- **A typed HTTP client** over the existing API, covering only what home needs: `GET /purchases`, `GET /merchants`, `GET /categories`. Errors are surfaced through the API's existing `ErrorResponse` shape rather than a second error vocabulary invented on the client.
- **A reference-data cache** for merchants and categories. `PurchaseView` carries `MerchantId` and `MerchantRaw` but no merchant *name*, and `ExpenseView` carries `CategoryId` but no category name, so any legible row requires a client-side join against a separately fetched dictionary.
- **The home page**: a month-to-date total, a conditional banner counting purchases in the `NeedsReview` extraction state, a list of recent purchases, and a sticky capture button.
- **A camera-backed capture control** on home: a real `<input type="file" accept="image/*" capture="environment">` under the user's finger. It selects a file and hands it onward; it does **not** upload. Uploading is the capture screen's job and is out of scope here.
- **Mobile viewport handling** as a first-class concern: dynamic viewport units, safe-area insets, and touch target sizing, verified on iOS Safari and Android Chrome.
- **A Vite dev proxy** to the API on `:5082`. The API has no CORS configuration and this change adds none — the proxy makes development same-origin, and production serving is deliberately left for a later change.

### Non-goals

Explicitly out of scope, to keep this change to one screen:

- Uploading an image, the capture wait state, candidate review, and confirmation into a purchase.
- A purchase detail screen. Rows on home are inert and are styled as content rather than as controls.
- Charts, category breakdowns and trends. A single month-to-date figure is the only aggregate on home.
- Editing reference data, merchants or categories.
- Authentication. The API has none and this client assumes a single trusted user on a private network.
- A PWA manifest, service worker or offline support.
- Production hosting, CORS, and the `docker-compose` service that would serve the built client.

## Capabilities

### New Capabilities

- `browser-client`: the human front door onto the ledger. Covers what the client is, how it reaches the API and presents the API's failures, how it resolves the display names the ledger deliberately does not denormalise, how it behaves on a phone, and what the home page shows and does.

### Modified Capabilities

None. The client consumes the HTTP surface exactly as `api-surface` already specifies it; no backend requirement changes.

## Impact

- **New**: `FE/` gains a Vite + React + TypeScript application — `package.json`, `tsconfig`, `vite.config.ts` (including the dev proxy), an application shell, a router, an HTTP client, a reference-data cache, and the home page with its tests.
- **Backend**: unchanged. No endpoint, contract or configuration is touched. The absence of CORS is worked around in development rather than fixed, and is recorded as a known constraint on deployment.
- **`docker-compose.yml`**: unchanged. Adding a service to build and serve the client belongs with the production-serving decision this change defers.
- **Root `README.md`**: gains a short section on running the client, since "a browser client in `FE/`" currently describes an empty directory.
- **Known constraint carried forward**: capture is synchronous and holds a request open across a third-party call to the fiscal verification portal. Home avoids the problem by not uploading, but the screen that does will have to face it.
