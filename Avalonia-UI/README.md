# Expenses — desktop client

The second human front door onto the ledger: an Avalonia desktop application for Windows, macOS and
Linux, beside the browser client in [`FE/`](../FE). Both offer the same launcher and the same
capture, review and confirm flow. The desktop takes a receipt from a file picker or from a file
dropped onto the window, where the browser uses the phone's camera. What it must do is specified in
the `desktop-client` capability; the decisions this README cites as `D1`–`D10` are in
[`openspec/changes/add-avalonia-desktop-client/design.md`](../openspec/changes/add-avalonia-desktop-client/design.md).

It talks only to the HTTP interface, and neither references nor is referenced by `BE/` or `FE/`.

## Running it

Bring the API up from the repository root, then run the client:

```bash
docker compose up -d --build                             # or ./up.sh
dotnet run --project Avalonia-UI/src/Expenses.Desktop
```

The client reads the ledger at `http://localhost:5082` unless told otherwise. To point it elsewhere,
set `EXPENSES_API`, or put `{ "Ledger": { "BaseAddress": "…" } }` in an `appsettings.json` next
to the executable. The environment variable wins over the file (D4).

## Layout

```
Expenses.Desktop.sln
Directory.Build.props  .editorconfig  global.json   copies of BE's, so the halves build apart (D9)
src/
  Expenses.Desktop.Core/      view models, ledger client, rules — no Avalonia reference (D1)
  Expenses.Desktop/           App, views (.axaml), ViewLocator, composition root, file picker
tests/
  Expenses.Desktop.Core.Tests/    view models and rules, over a fake HTTP transport
  Expenses.Desktop.UI.Tests/      the real views on Avalonia's headless platform, ledger faked
  Expenses.Desktop.E2E.Tests/     the real views against the real API and database
```

MVVM is CommunityToolkit.Mvvm's source generators and nothing more (D2). Navigation is one slot on
`MainWindowViewModel` that the `Navigator` fills, and `ViewLocator` lists which view draws which
screen explicitly (D5). The month, formatting, label and review rules are ports of the browser
client's, each with the browser client's own test cases (D7).

## The three test suites

```bash
./test-ui.sh               # view models and headless UI: no display, no Docker
E2E=1 ./test-ui.sh         # the end-to-end suite, stack and all
```

The view-model suite loads no UI platform, and the headless suite needs no display, so both run on a
CI agent as they are. The end-to-end suite brings `docker-compose.e2e.yml` up on an empty database
and tears it down afterwards (`KEEP=1` leaves it running). That stack publishes port 5082, so stop a
running development API first. Anything after `--` goes to `dotnet test`:
`./test-ui.sh -- --filter HomeViewModel`.

This half uses xunit v3 while `BE/` stays on 2.9, because Avalonia 12's headless test integration is
built on v3. The halves never share a solution, so the two versions never meet.

## What headless tests cannot see

The headless platform has no operating system file dialog, no real drag source and no platform focus
rules, so on each operating system, before relying on a build, check by hand:

- **Pick.** Capture opens the system file picker showing images, and "All files" can still be
  chosen. Cancelling it leaves home as it was.
- **Drop one.** Dragging one image from the file manager onto the window opens capture for it.
- **Drop two.** Dragging two files at once captures neither and says receipts go one at a time.
- **Keyboard walk.** Tab reaches every control on home and review, with a visible focus ring, and
  Enter and Space activate buttons.
- **Minimum size.** The window will not shrink below the size at which capture and the month's total
  both stay in view.
