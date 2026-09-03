## Context

See `proposal.md` — Why. The constraints that shape this design come from the backend as it already stands:

- **The HTTP surface is fixed.** `GET /purchases?from&to&skip&take`, `GET /merchants`, `GET /categories` are what home reads. There is no summary or aggregation endpoint, and this change adds none.
- **`PurchaseView` is deliberately not denormalised.** It carries `MerchantId` and `MerchantRaw`, never a merchant name; `ExpenseView` carries `CategoryId`, never a category name. This follows D9 — "a later match must never erase what was printed" — so the join belongs on the client, not in a new backend projection.
- **`ExtractionState` is `Extracted | NeedsReview | Failed`.** `Pending` and `Extracting` were removed when extraction became synchronous, so home never has to poll or represent in-flight extraction.
- **The API has no CORS configuration and no authentication.** Both are true today and neither is changed here.
- **`OccurredAt` is a `DateTime` with no offset**, i.e. wall-clock time, and the deployment is a single user in a single timezone.

`FE/` is empty, so every decision below is a first one — there is no existing front-end convention to follow or violate.

## Goals / Non-Goals

**Goals:**

- A shell that the capture and review screens can be added to without being restructured — routing, data access and layout primitives in place, even though only one route exists.
- Home works as a launcher on a phone held one-handed, and keeps working when the network does not.
- The mobile constraints that are easy to break silently — the camera gesture, image fidelity, safe areas — are pinned down by tests rather than by memory.

**Non-Goals:**

- A design system, theming, or dark mode. One screen does not justify either, and inventing tokens before the second screen exists guesses wrong.
- Type generation from the OpenAPI document. Three endpoints do not pay for a codegen step; see D14.
- Any abstraction over the API beyond a thin typed fetch wrapper. There is no second backend to abstract from.

## Decisions

### D1 — Home is a launcher, not a dashboard

Capture is the primary action and gets the visual weight; recent purchases are supporting information; the month-to-date figure is a single number in the header.

**Why:** the use case is standing at a till with a paper receipt. A dashboard buries the one verb behind numbers nobody opened the app to read. It also has a practical consequence: because the button never depends on fetched data, every data failure degrades the supporting act while the app's actual purpose keeps working.

**Alternative rejected:** a spend dashboard with charts. It would need the aggregation question resolved, a charting dependency and a category colour system before rendering anything real — and would land a screen full of placeholders.

### D2 — The capture button is bottom-anchored and sticky

Not centred in the screen.

**Why:** on a large phone held one-handed, the vertical centre is above the comfortable thumb arc; the bottom is the easiest region to reach. Sticky rather than in-flow means it is reachable whether or not the list is scrolled, and never scrolls away.

### D3 — The capture control is a native file input on the home route; navigation happens after selection

Home renders a visually hidden `<input type="file" accept="image/*" capture="environment">` inside a `<label>`. The route change to `/capture` is dispatched from the input's `change` handler, carrying the `File`.

**Why:** this is a hard platform constraint, not a preference. iOS Safari only opens the camera when the input is activated within the user-gesture task. Navigating first and firing the input from an effect on the destination route spends the gesture and iOS silently does nothing — no error, no camera. Android is more permissive, which makes this exactly the kind of bug that passes desktop and Android testing and fails on the one device that matters.

The consequence is that home is not purely a launcher: it owns the input. That is a good split anyway — the slow part (a synchronous upload that holds a request open across the fiscal portal call) lives on `/capture`, so backgrounding the tab during that wait cannot disturb home.

**Alternatives rejected:** `getUserMedia` with an in-page camera — far more code, worse capture quality than the native camera app, and no benefit here. A `/capture` route that triggers the input on mount — broken on iOS, as above.

### D4 — Captured bytes are never resized or re-encoded on the client

The `File` is passed through untouched.

**Why:** `receipt-ingestion` requires that a decodable fiscal symbol be read "from an unmodified photograph … at full resolution", explicitly not depending on cropping or rectification. Fiscal QR codes on thermal paper are dense and marginal; downscaling turns decodable receipts into ones that fall through to the probabilistic stages. Worse, the regression is invisible: a decode miss is by design indistinguishable from a receipt carrying no code. The bandwidth saving is not worth silently degrading extraction.

### D5 — Home fetches the current month, not a fixed page

`GET /purchases?from=<first of month>`, then derive the total, the review count and the recent slice from that one response.

**Why:** it answers all three of home's questions in one round trip with no aggregation endpoint. A personal ledger is tens to low hundreds of purchases a month — on the order of 100–300 KB with expense lines included. Noticeable on cell data, not painful.

**Alternative rejected:** `take=10`. Smaller and faster, but makes the month total impossible without a second call, and the total was explicitly wanted.

**Consequence to accept:** the "needs review" count is scoped to the current month, not the whole ledger. This is stated in the spec rather than hidden — a receipt needing review that is more than a month old will not be counted. Revisit if it proves to matter.

### D6 — Reference data is a separately cached dictionary, joined on the client

`GET /merchants` and `GET /categories` are fetched once, cached with a long stale time, and joined by id at render. The label resolution is a fixed chain:

```
  merchantName(MerchantId)  ??  MerchantRaw  ??  "Unknown merchant"
```

**Why:** it is forced by D9 in the domain, and the fallback is not a workaround — showing the verbatim receipt text for an unmatched merchant is the honest display, and doubles as a visible signal that the merchant has not been learned yet. The dictionary is small and slow-changing, and serves every future screen.

Reference data failing must not fail the page: purchases still render on `MerchantRaw`. This is why it is a separate query rather than part of a combined load.

### D7 — TanStack Query for server state

**Why:** the two things it provides are both concretely needed here rather than speculative. First, invalidation: confirming a capture has to refresh home's list and total, and that is the very next screen. Second, `refetchOnWindowFocus` — on mobile, returning from the camera app or from a backgrounded tab is the common way to arrive at this screen, and a stale list is exactly the failure mode ("did I already log that?") the list exists to prevent.

**Alternative rejected:** React's `use()` with a Suspense boundary. Leaner, and genuinely sufficient for one read-only screen, but leaves invalidation and focus-refetch to be hand-rolled the moment the second screen lands. Retrofitting a cache under existing components is worse than starting with one.

### D8 — React Router with a single route

**Why:** the second route is not hypothetical — D3 already navigates to `/capture`. On mobile the OS back gesture is the primary navigation, so real history entries matter more than on desktop. Adding the router later means rewriting the shell.

### D9 — Vite dev proxy; no CORS change to the backend

`/api` proxied to `http://localhost:5082` in `vite.config.ts`, with the client always calling same-origin relative paths.

**Why:** it makes development same-origin, so no backend change is needed for this change to be usable, and it keeps the client's request code identical in development and in a same-origin production deployment. Deciding how the built client is served in production — static files behind the API, a separate nginx service, or a separate origin with CORS — is a real decision with its own trade-offs and does not need to be made to build one screen.

**Risk accepted and recorded:** the client is not deployable as-is. This is stated in the proposal's non-goals rather than left to be discovered.

### D10 — CSS Modules with custom properties; no CSS framework

**Why:** the layout problems this screen actually has are `dvh`, `env(safe-area-inset-*)`, sticky positioning and a scroll container. These are plain CSS problems, and a utility framework adds a dependency and a build step without solving any of them. Custom properties give a place for spacing and colour to live so that the second screen has something to follow.

### D11 — `100dvh` and `env(safe-area-inset-*)`, with `viewport-fit=cover`

`100vh` is wrong on iOS Safari — it does not account for the collapsing toolbar, so a full-height layout overflows and a bottom-anchored control is pushed under the browser chrome. `100dvh` tracks the visible viewport. The safe-area env vars read as zero unless `viewport-fit=cover` is set in the viewport meta tag; both are needed or neither works.

### D12 — Dates are treated as local wall-clock

`OccurredAt` is a `DateTime` with no offset. The client parses it as local time, and computes "today"/"yesterday" and the month boundary against the device's local date.

**Why:** the backend stores wall-clock time for a single-timezone, single-user deployment. Coercing it to UTC on the client would shift purchases across day boundaries — a purchase at 21:00 showing as "yesterday" is a visible, confusing bug. Parsing an offset-less string is the one place a naive `new Date()` differs meaningfully across browsers, so it is worth being explicit about rather than incidental.

### D13 — Formatting lives in one module, `en-GB` + `EUR`

`Intl.NumberFormat('en-GB', { style: 'currency', currency: 'EUR' })` and a small relative-date helper, both in a single module with the locale as a constant.

**Why:** one module means one place to change when the locale becomes configurable, and it keeps `Intl` calls out of components where they would be constructed per render. Formatting is presentation-only — no rounding or arithmetic happens on a displayed amount.

### D14 — API types are hand-written, not generated

TypeScript interfaces mirroring `PurchaseView`, `ExpenseView`, `MerchantView`, `CategoryView` and `ErrorResponse`, written by hand in one module.

**Why:** three endpoints and five shapes do not justify a codegen step, a generator dependency, and a build-time dependency on a running API. The cost of this decision is real and worth naming: the types can drift from the backend silently. Revisit when the client consumes enough of the surface that hand-maintenance is the larger cost — most likely once capture and confirm land, since `CaptureResult` and the extraction views are considerably more intricate than anything home reads.

### D15 — Testing: Vitest and Testing Library for behaviour; real devices for what they cannot see

Each spec scenario becomes a test where it is observable in jsdom. Three things are not: the camera actually opening, safe-area insets, and the collapsing-toolbar viewport. Those are a written manual checklist run on a real iPhone and a real Android device, not simulated.

The gesture constraint in D3 is the one worth guarding in code — a test asserting that activating the capture control triggers the file input directly, with no navigation between, so a later refactor that introduces one fails the suite rather than silently breaking iOS.

## Risks / Trade-offs

**The iOS gesture constraint is re-broken by a future refactor** → Guarded by a test asserting no navigation precedes input activation (D15), and the reason is recorded in D3 rather than living in someone's memory.

**Client-side downscaling is reintroduced as an "optimisation"** → D4 records why not, and the harm is invisible in testing. The mitigation is documentation, honestly; a test cannot detect a QR that would have decoded.

**Month payload grows uncomfortable** → Acceptable at personal-ledger volume. If it stops being so, the fix is a summary endpoint on the backend, which is a backend change and out of scope here. Not pre-optimised.

**The "needs review" count misses receipts older than the current month** → Accepted and stated in the spec (D5). Reconsider once a purchase list screen exists to see them on.

**Hand-written types drift from the backend contract** → Accepted deliberately (D14), with a named trigger for revisiting.

**The client is not production-deployable** → Accepted (D9). Development works fully; deployment is a separate decision, recorded in the proposal's non-goals so it is not mistaken for an oversight.

**HEIC cannot be rendered in an `<img>` on Android** → Does not affect home, which never displays a captured image. Carried forward as a known issue for the capture screen, which will want a preview.

## Open Questions

- Whether the app is ever exposed beyond a private network. It has no authentication because the API has none; if that changes, it is a backend decision first and the client follows. Deferrable — it changes nothing about home.
- Whether an add-to-home-screen manifest is wanted. Cheap to add later and it changes no component; excluded here only to keep scope to one screen.
