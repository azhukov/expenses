## Why

The ledger has one human front door today, the React browser client in `FE/`, built for a phone held in one hand at the point of purchase. Everything the backend offers a person already goes through `Expenses.Api`, which leaves room for a second client that makes no demands on the first. Doing that in Avalonia keeps the new client in the language the rest of the repository is written in, lets its view models be specified and tested with the same xUnit discipline as the backend, and puts the same ledger on a desktop, where a keyboard, a large window and drag-and-drop make reviewing and correcting a receipt quicker than on a phone.

## What Changes

- **New `Avalonia-UI/` folder at the repository root**: an Avalonia desktop application using the built-in Fluent theme, running on Windows, macOS and Linux. It is a sibling of `FE/` and `BE/` and neither references nor is referenced by either.
- **Feature parity with the browser client**: the same launcher home (a prominent capture action, the month-to-date total, recent purchases, the review count, degraded reads with retry), and the same capture flow (upload on arrival, review and edit candidates, extraction problems surfaced, manual entry when extraction fails, confirm, rejection reported in place, abandoning leaves no trace).
- **Capture adapted to a desktop**: the camera and phone-viewport requirements are replaced by choosing an image file from a picker or dropping one onto the window. The rules that the original bytes are sent unmodified and never pre-judged carry over unchanged.
- **MVVM with CommunityToolkit.Mvvm** (MIT): source-generated observable properties and commands; `Microsoft.Extensions.DependencyInjection` for composition; a small view locator for screen navigation. No other MVVM, navigation or form framework.
- **The client talks only to the HTTP interface**, through a typed `HttpClient` against a configurable base address. It owns its own request and response types rather than referencing `BE/`.
- **Three test layers**: view-model unit tests (xUnit, fake HTTP handler); UI tests that render the real views with `Avalonia.Headless`, driven through their controls; and an end-to-end suite that runs the headless UI against the real API and database, mirroring `test-e2e.sh`.
- **Its own style gate**: the build strictness of `BE/Directory.Build.props` and `BE/.editorconfig` (warnings as errors, code style enforced in build), plus compiled bindings throughout.
- **CI gains a third half**: the `changes` job reports `ui` alongside `fe` and `be`, new `ui-style`, `ui-unit` and `ui-e2e` jobs run when `Avalonia-UI/` changes, and `gate` waits on them.

No backend, API contract or browser-client change is required.

## Capabilities

### New Capabilities

- `desktop-client`: the Avalonia desktop front door onto the ledger. It covers the launcher home, choosing or dropping a receipt image, the upload-review-confirm flow, degraded reads, formatting, and fitting a resizable desktop window. The behaviour matches `browser-client` wherever the device does not change it.

### Modified Capabilities

(none — `browser-client` is untouched; the desktop client is specified alongside it, not as a variant of it)

## Impact

- **New code**: `Avalonia-UI/` containing the application, a view-model library with no Avalonia UI dependency, and their test projects, with a solution, `Directory.Build.props`, `.editorconfig` and `global.json` of its own.
- **Dependencies (all free, OSS licenses)**: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Headless.XUnit`, `CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Http`, plus the xUnit and Testcontainers packages the backend already uses.
- **CI**: `.github/workflows/ci.yml` gets new path-filter output and jobs; the required `gate` check depends on them.
- **Scripts and docs**: a `test-ui.sh` runner next to `test-fe.sh`/`test-be.sh`; the root `README.md` names the third client; `Avalonia-UI/README.md` explains running it against the local stack.
- **Out of scope**: packaging, installers, signing and auto-update; mobile or WebAssembly Avalonia targets; decoding a receipt's fiscal QR on the client; any backend change.
