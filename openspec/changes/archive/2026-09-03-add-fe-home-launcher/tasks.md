Test-first throughout, per the project's working discipline. Sections that produce behaviour open at `X.0` with the failing tests for that section; no implementation task in such a section may be started while its `X.0` is unchecked. Sections marked **(exempt)** produce no behaviour — scaffolding, configuration, documentation — and deliberately carry no test task rather than inventing one.

Scenario names in parentheses refer to `specs/browser-client/spec.md`.

## 1. Project scaffolding (exempt — no behaviour)

- [x] 1.1 Scaffold a React + TypeScript application in `FE/` with Vite; `npm run dev` serves it and `npm run build` produces a bundle.
- [x] 1.2 Configure TypeScript in strict mode, and add ESLint with the React and hooks rule sets.
- [x] 1.3 Add Vitest, Testing Library and `jsdom`, with a `test` script. Confirm the runner executes a trivial passing test, then delete it.
- [x] 1.4 Add the `/api` dev proxy to `http://localhost:5082` in `vite.config.ts` (D9). Verify by requesting `/api/categories` through the dev server against a locally running API.
- [x] 1.5 Add TanStack Query and React Router as dependencies (D7, D8).
- [x] 1.6 Set the viewport meta tag with `viewport-fit=cover`, and add a base stylesheet defining spacing and colour custom properties plus a CSS reset (D10, D11).
- [x] 1.7 Add the application shell: query client provider, router with `/` as the only route, and an error boundary at the root.
- [x] 1.8 Hand-write the API types mirroring `PurchaseView`, `ExpenseView`, `MerchantView`, `CategoryView` and `ErrorResponse` (D14).

## 2. Formatting amounts and dates

- [x] 2.0 Write failing tests for the formatting module: an amount renders in euro with two decimal places (*An amount is formatted*); a purchase occurring today is described as today and one occurring yesterday as yesterday (*A recent date is described in relative terms*); an older purchase shows a date (*An older date is shown as a date*); a formatted amount parses back to the value given (*Formatting does not change values*); an offset-less timestamp at 21:00 is treated as local wall-clock and does not shift across a day boundary (D12).
- [x] 2.1 Implement the formatting module with `en-GB`/`EUR` as constants and memoised `Intl` instances (D13).
- [x] 2.2 Implement local wall-clock parsing of `OccurredAt` and the relative-date helper (D12).

## 3. Reading the ledger over HTTP

- [x] 3.0 Write failing tests for the API client against a mocked transport: a successful read returns typed data; a failure carrying an `ErrorResponse` surfaces that response's human-readable message (*The ledger reports a specific error*); a transport failure with no response body surfaces a generic failure without inventing an error code; a successful empty result is data rather than an error (*An empty ledger is not a failure*).
- [x] 3.1 Implement the typed fetch wrapper over relative `/api` paths, parsing `ErrorResponse` and preserving its message.
- [x] 3.2 Implement the `purchases`, `merchants` and `categories` read functions.
- [x] 3.3 Define the query hooks: purchases for the current month, and reference data with a long stale time as an independent query so its failure cannot fail the page (D6).

## 4. Resolving display names

- [x] 4.0 Write failing tests for label resolution: a purchase whose merchant the dictionary describes shows the display name (*A known merchant is named*); one with verbatim text but no merchant reference shows the verbatim text (*An unmatched merchant falls back to what was printed*); one with neither states the merchant is unknown and is still listed (*Neither a merchant nor verbatim text*); no identifier ever appears in resolved output (*Identifiers are never displayed*).
- [x] 4.1 Implement the merchant label resolver as the fixed fallback chain in D6.
- [x] 4.2 Implement the category name lookup against the categories dictionary, using the same fallback discipline.

## 5. Deriving the month's figures

- [x] 5.0 Write failing tests for the derivations over a fixture month: the total sums only purchases occurring in the current calendar month (*The total reflects the current month*); an empty month totals zero (*No purchases this month*); the review count counts only purchases whose extraction state is `NeedsReview`; a month with none yields a count of zero (*Nothing needs review*); the recent slice preserves most-recent-first ordering.
- [x] 5.1 Implement the month-boundary query parameters against the device's local date (D12).
- [x] 5.2 Implement the total, the review count and the recent slice as derivations over the single month response (D5).

## 6. The capture control

- [x] 6.0 Write failing tests for the capture control: it renders a file input carrying `accept="image/*"` and `capture="environment"` (*Camera opens on activation*); activating the control triggers that input directly, with no navigation dispatched before it (*No intermediate step precedes the camera*, D3); selecting a file navigates to `/capture` carrying the exact `File` object, unresized and unmodified (*Original bytes are preserved*, *Home does not submit the image*); a file of an undisplayable type is still carried forward without error (*An unusual format is not pre-judged*); dismissing without selecting leaves the screen unchanged and submits nothing (*Capture is abandoned*).
- [x] 6.1 Implement the control as a visually hidden file input inside a label, with no programmatic click and no work between activation and the browser's own handling (D3).
- [x] 6.2 Implement the `change` handler that navigates to `/capture` with the `File` in router state, passing the object through untouched (D4).
- [x] 6.3 Add a placeholder `/capture` route that renders the name of the received file and nothing else, so the navigation is verifiable. It performs no upload — that is a later change.

## 7. The home screen

- [x] 7.0 Write failing tests for the home screen against mocked queries: capture is present and is the only prominent control, without scrolling (*Capture is reachable on arrival*); it remains present when the purchases query fails (*Capture survives a failure to read the ledger*, *The ledger cannot be reached*); a loading indicator shows before data arrives (*Loading is visible*); an empty ledger shows the empty state with capture present (*An empty ledger*); recent purchases render most-recent-first with merchant, amount, when, and line count (*Recent purchases are shown*); a purchase with a receipt is distinguishable from one without (*A purchase carrying a receipt is distinguishable*); rows are not activatable and expose no control affordance (*Entries are not controls*); the review banner appears with a count when purchases need review (*Purchases needing review exist*) and is absent entirely when none do (*Nothing needs review*); no chart, category breakdown or period comparison is rendered (*No breakdown accompanies the total*); a failed read offers a retry and a successful retry clears the failure and shows purchases (*Retrying succeeds*); reference data failing still lists purchases on verbatim text and reports no ledger failure (*Reference data cannot be read*).
- [x] 7.1 Implement the purchase row: label from section 4, amount and relative date from section 2, line count, and the receipt indicator.
- [x] 7.2 Implement the recent list with its loading, empty and failed states, including the retry affordance.
- [x] 7.3 Implement the header with the month-to-date total, and the conditional review banner.
- [x] 7.4 Compose the home route from the header, banner, list and capture control.

## 8. Fitting the device

- [x] 8.0 Write failing tests for what jsdom can observe: every interactive control on home declares a touch target of at least 44 by 44 CSS pixels (*Touch targets are large enough*); the capture container is positioned so that it does not scroll with the list (*Capture stays reachable while browsing recent purchases*).
- [x] 8.1 Implement the page layout: `100dvh` root, a scrolling list region, and the capture control anchored below it (D11).
- [x] 8.2 Apply `env(safe-area-inset-*)` padding to the header and the capture container (D11).
- [x] 8.3 Ensure no horizontal overflow at narrow viewports — constrain long merchant text rather than letting it widen a row (*No horizontal scrolling*).
- [x] 8.4 Write the manual device checklist covering what jsdom cannot observe, and record its results: the camera opens on a real iPhone and a real Android device; the capture control stays fully visible and tappable as the browser toolbar collapses (*The browser interface collapses*); it clears the home indicator (*The bottom of the screen is not obscured*); text does not trigger browser zoom (D15).

## 9. Documentation (exempt — no behaviour)

- [x] 9.1 Add `FE/README.md`: how to run the client, that it expects the API on `:5082` through the dev proxy, and that it is not yet production-deployable (D9).
- [x] 9.2 Update the root `README.md` so the reference to "a browser client in `FE/`" points at something real.
