## Context

See `proposal.md` — Why. The constraints that shape this design come from what already exists:

- **The HTTP surface is fixed and sufficient.** The home screen reads `GET /purchases?from&to`, `GET /merchants` and `GET /categories`. The review screen also reads `GET /units` and writes `POST /receipts/capture` (multipart, one `file` part) and `POST /purchases` (JSON, with a `capture` echo). The browser client does everything in the `desktop-client` spec using only these endpoints, so this change adds none.
- **Every failure has one shape**, `ErrorResponse`, whose `message` is written for a person. `OccurredAt` is offset-less wall-clock time (browser-client D12). `PurchaseView` carries `MerchantId` and `MerchantRaw`, never a name, so the client joins against the dictionary.
- **The API has no CORS configuration and no authentication.** Native `HttpClient` needs no CORS, so neither changes here.
- **The browser client's rules have been worked out already.** `FE/src/month`, `format`, `labels` and `capture/review.ts` hold them as small pure functions, with tests. The desktop client ports each rule together with its test cases instead of rediscovering it.
- **The repository's gate is strict.** The backend builds with `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` and StyleCop SA1402. CI filters each half by path, and one required `gate` job waits on every suite (see `.github/workflows/ci.yml`).

## Goals / Non-Goals

**Goals:**

- Every behaviour in the spec is decided in a view model, which can be tested with no window, no dispatcher and no network.
- The views are thin enough that headless UI tests only need to prove wiring, layout and keyboard reach.
- `Avalonia-UI/` builds, tests and gates on its own. Deleting the folder and its CI jobs would leave the rest of the repository untouched.

**Non-Goals:**

- Sharing code with `FE/` or `BE/`. Duplicating about a dozen response records costs less than the coupling a shared contracts project would bring (D3).
- Custom styling beyond the Fluent theme's defaults, a light or dark switch, or localisation. The Fluent theme follows the OS light or dark setting at no cost, and that is enough.
- Caching or offline reads. Home reads again on arrival and after a confirmation, and that is all the spec asks for.

## Decisions

### D1 — Four projects, split by what may reference Avalonia

```
Avalonia-UI/
├── Expenses.Desktop.sln
├── Directory.Build.props  .editorconfig  global.json
├── src/
│   ├── Expenses.Desktop.Core/      view models, ledger client, rules — no Avalonia reference
│   └── Expenses.Desktop/           App, views (.axaml), view locator, composition root
└── tests/
    ├── Expenses.Desktop.Core.Tests/    xUnit: view models and rules, fake HttpMessageHandler
    ├── Expenses.Desktop.UI.Tests/      Avalonia.Headless.XUnit: real views, fake ledger
    └── Expenses.Desktop.E2E.Tests/     Avalonia.Headless.XUnit: real views, real API
```

**Why:** keeping Avalonia out of `Core` enforces the first goal at compile time. A view model can't read a control even by accident, and the fast test project doesn't load Avalonia at all. `CommunityToolkit.Mvvm` has no UI dependency, so it lives in `Core`.

**Alternative rejected:** one app project with view models in a folder. It works until someone reaches into `Avalonia.Controls` from a view model to "just check" something, and then the fast suite needs a headless platform.

### D2 — CommunityToolkit.Mvvm, with its generators and nothing more

View models are `partial` classes deriving from `ObservableObject`. State uses `[ObservableProperty]` partial properties, actions use `[RelayCommand]`, and async commands use their built-in `IsRunning`, `CanExecute` and cancellation. `WeakReferenceMessenger` is **not** used. Navigation goes through an explicit service (D5), so every flow of control can be read from a constructor.

**Why:** the spec's behaviours are property changes and commands, which is exactly what the toolkit generates. Async commands cover two of the spec's cases for free. "Confirming cannot be repeated while outstanding" falls out of `AllowConcurrentExecutions = false`. "The wait is visible" binds to `IsRunning`. View models hold plain `string`, `decimal` and `DateTime` values, with no reactive wrappers.

**Alternatives rejected:** ReactiveUI. It is powerful for stream-shaped state, but none of this state is stream-shaped, and `TestScheduler` would turn every view-model test into a lesson in Rx. Prism is no longer simply free, and its regions and modules solve problems that two screens don't have.

### D3 — The client owns its contract types

`Core/Ledger/` declares the request and response records it sends and reads (`PurchaseView`, `CaptureResult`, `RecordPurchaseRequest`, `ErrorResponse`, …), matching the API's camelCase JSON and enums-as-names. It mirrors `FE/src/api/types.ts`, not `BE/Expenses.Application/Dtos`.

**Why:** a project reference into `BE/` would make every backend DTO edit a change to this half too. It would defeat CI path filtering and pull the Application layer's dependencies into a desktop app. Drift is caught where it matters: the E2E suite (D8) runs against the real API.

**Alternative rejected:** a new shared `Expenses.Contracts` project. It would be right if a third .NET consumer appeared. For one consumer, it is a refactor of `BE/` that this change has no reason to carry.

### D4 — One `LedgerClient` over a typed `HttpClient`, failures as one exception

`LedgerClient` exposes the seven calls the screens need. It is registered with `AddHttpClient`, and its base address comes from configuration: `appsettings.json` next to the executable, overridable by the `EXPENSES_API` environment variable, defaulting to `http://localhost:5082`. Every non-success response whose body parses as `ErrorResponse` becomes a `LedgerException` carrying its `message`, `code` and status. Transport failures become a `LedgerException` with no code. The capture upload sends the bytes it is given unchanged, as one `file` part in `MultipartFormDataContent`, with the file's name. It sets no part content type beyond what the file name implies, and never decodes the image.

**Why:** it mirrors `FE/src/api/client.ts`, so "no second error vocabulary" means the same thing in both clients. Never decoding the image is what guarantees "original bytes are preserved": the client never holds a bitmap that could be re-encoded.

### D5 — Navigation is a current view model and a view locator

`MainWindowViewModel.Current` holds the active screen: `HomeViewModel` or `CaptureViewModel`. The window's `ContentControl` binds to it, and a `ViewLocator` data template maps `XViewModel` to `XView` by type, registered explicitly rather than found by reflection so trimming can't break it. `INavigator` exposes `ToHome()` and `ToCapture(IStorageFile)`. Screens are created by a factory from the DI container.

**Why:** two screens and one hand-off don't justify a routing library. Registering views explicitly keeps compiled bindings and trimming honest.

### D6 — Capture: `IStorageProvider` picker and window-level drop, behind one abstraction

`Core` defines `IImagePicker` (returns one `ReceiptImage` or `null`) and `ReceiptImage` (a name plus a function that opens its read stream). The app implements the picker with `TopLevel.StorageProvider.OpenFilePickerAsync`, using an image filter plus "All files". The home view handles `DragDrop` and passes the view model the dropped file list, and `HomeViewModel` decides: exactly one file captures it, several produce the one-at-a-time message, none does nothing.

`CaptureViewModel` reads the image into a `byte[]` **once**, on arrival, and uploads (and retries) from that buffer as a `ByteArrayContent`.

**Why:** the spec's drop rules are behaviour, so they belong in a view model that can be tested with plain lists. The picker can't run headless, so it sits behind an interface that UI tests replace. Reading once means "a retry submits the same bytes that were originally chosen" holds even if the file changes or disappears on disk after being picked. Receipt photographs are a few megabytes, so holding one in memory costs nothing that matters.

**Alternative rejected:** opening the file stream again for each attempt. It saves memory, but a retry would upload whatever is on disk now, not what was chosen.

### D7 — Rules are ported from `FE/` together with their test cases

`MonthRules` (current-month range, month total, review count, recent eight), `Formatting` (euro with two decimals in en-GB, today/yesterday, wall-clock parsing), `Labels` (merchant display name fallback) and `ReviewRules` (review reasons, low-confidence per line, building the confirmation from the capture plus edits) become static classes in `Core`. Each FE test case, including the month-boundary and float-accumulation cases, becomes an xUnit `[Theory]` row, written before the port.

Money is `decimal` end to end, so the FE's "round the float sum to cents" becomes unnecessary rather than ported. The editable line keeps every numeric field as the `string` the user typed, parsed only when confirming, for the same reason the FE does: "a half-typed `3.` is not a number".

The review date is a text field in one fixed format, `yyyy-MM-dd HH:mm`, never the computer's regional format. The browser client sends its `datetime-local` string as it is. The request here carries a `DateTime?`, so text that won't parse can't be handed to the API. Confirming therefore reports the expected format and sends nothing. The alternative, sending no date, would silently let the receipt's date replace the one the user typed. This is the only check the client makes before the ledger's own. A number that won't parse is still sent as zero, for the ledger to refuse in its own words, as in the FE.

**Why:** these rules are already specified and proven. Porting them test-first is cheaper and safer than deriving them again from the spec wording.

### D8 — Three test layers, each with one job

| Layer | Runs | Talks to | Proves |
|---|---|---|---|
| `Core.Tests` | `dotnet test`, no display | a `FakeHttpMessageHandler` | every spec scenario that is a decision: states, fallbacks, rules, request shapes |
| `UI.Tests` | `[AvaloniaFact]` on the headless platform | a fake `LedgerClient` handler, a fake `IImagePicker` | wiring: bindings resolve, commands reach controls, capture is visible at minimum window size, no horizontal scroll, keyboard focus reaches every control, `AutomationProperties.Name` present, entries are not focusable or selectable |
| `E2E.Tests` | `[AvaloniaFact]` on the headless platform | the real API and PostgreSQL from `docker-compose.e2e.yml` | capture → review → confirm → home shows the purchase, with a real receipt fixture |

UI tests find controls by `x:Name` or automation name and drive them the way a user would: `KeyPress`, `KeyTextInput`, `MouseDown` on the headless window, not direct command execution. Layout assertions use `Bounds` after `Dispatcher.UIThread.RunJobs()` and, where useful, `CaptureRenderedFrame()` for a failure artifact.

The E2E suite reuses `docker-compose.e2e.yml` and the receipt fixtures in `FE/e2e/fixtures` by path, and a `test-ui.sh` mirrors `test-e2e.sh` (stack up on an empty database, run, tear down, `KEEP=1`).

**Why:** it has the same shape as the FE (vitest / Playwright) and the BE (unit / integration), so a reader of any half recognises it. Headless for both upper layers means CI needs no display server.

**Alternative rejected:** E2E through `WebApplicationFactory<Program>` in-process. Faster, but it needs a project reference into `BE/` (see D3), and the compose stack is already what the FE E2E suite trusts.

### D9 — The half's own build rules, copied not moved

`Avalonia-UI/Directory.Build.props` copies the backend's (net10.0, nullable, warnings as errors, code style in build, StyleCop with only SA1402), and adds `<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>`. `.editorconfig` copies `BE/.editorconfig`'s rule set. An exclusion for `*.g.cs` generated files is added only if a toolkit or Avalonia generator trips a rule, and only for that rule.

**Why:** moving the files to the root would change `BE/` in a change that promises not to. Compiled bindings by default turn a mistyped binding path into a build error instead of a silent blank field. That is the XAML equivalent of the FE's `tsc`.

### D10 — CI: a third half

The `changes` job gains `ui=true` for `^Avalonia-UI/`. The shared set that marks every half (the workflow file, compose files) now also marks `ui`, and `test-ui.sh` counts as shared with `ui`. New jobs:

- `ui-style`: `dotnet format --verify-no-changes` and a build with `-p:GenerateDocumentationFile=true`, matching `be-style`.
- `ui-unit`: `Core.Tests` and `UI.Tests` on `ubuntu-latest`, needing no display.
- `ui-e2e`: `E2E=1 ./test-ui.sh`. It runs when `Avalonia-UI/` changes **or when `BE/` changes**, as the browser `e2e` job does. A changed API contract breaks this client too, and this is the suite that would notice.

`gate`'s `needs` list gains all three. The existing "skipped counts as passing" logic already handles halves a PR doesn't touch.

## Risks / Trade-offs

- **[Headless does not exercise the real picker, drag source or OS focus rules]** → The picker is behind `IImagePicker`, and drop is fed synthetic `DragEventArgs` in UI tests. A short manual checklist in `Avalonia-UI/README.md` (pick, drop one, drop two, keyboard walk) covers what headless can't, on each OS before a release.
- **[Analyzer rules fire on generated code]** (partial properties, Avalonia's `InitializeComponent`) → The first scaffold task builds an empty view model and view under the full rule set before any behaviour is written, so exclusions are decided once, narrowly, and early.
- **[Contract drift between `BE/` DTOs and the client's records]** → The E2E suite fails on any shape that matters to a spec scenario. Drift in fields the client doesn't read is harmless by construction (`System.Text.Json` ignores unknown members).
- **[Two xunit majors in one repository]** `Avalonia.Headless.XUnit` 12.x is built on xunit v3, and only the 11.3 line supports the xunit 2.9 that `BE/` uses → This half uses Avalonia 12 and xunit v3 across all three of its test projects, and `BE/` stays on 2.9. The halves already build, test and gate separately, so the split never meets inside one solution. Starting a new client on the previous Avalonia major would only schedule a migration.
- **[A long synchronous capture request with no timeout]** → As in the browser client, the wait is visible and not bounded. The one desktop-specific addition is that going back cancels the command's token, so a late response can't open review ("Leaving while extraction is running").
- **[Two clients to keep in step]** → The specs are parallel on purpose, and requirement names match `browser-client` wherever behaviour is shared. A change to a shared requirement should update both specs in the same change.

## Migration Plan

Additive only. Nothing is deployed: the client is run with `dotnet run --project Avalonia-UI/src/Expenses.Desktop` against the local stack. Rolling back means deleting the folder and the three CI jobs.
