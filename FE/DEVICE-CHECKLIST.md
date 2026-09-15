# Manual device checklist

Three things the home screen depends on cannot be observed in jsdom: whether the camera actually
opens, what the safe-area insets resolve to, and how the layout behaves as the browser toolbar
collapses. They are checked by hand on a real iPhone and a real Android device, not simulated
(D15). The rest of the behaviour is covered by `npm test`.

Run it with `FE_HOST=1 ./up.sh` from the repository root. It serves the page and the API over HTTPS
at `LAN_HOST` (`<machine-name>.local`, or an IPv4 address you set it to) and prints both URLs.
Over plain HTTP a LAN address is not a secure context, so check 1 would fail for that reason alone,
and the page's calls to the API would be blocked as mixed content.

Before the checks, on each device:

1. Open the API URL `up.sh` prints (`https://<LAN_HOST>:5443/openapi/v1.json`) and accept the
   certificate warning.
2. Open the page (`https://<LAN_HOST>:5173`) and accept its warning.

If home says the ledger could not be reached, step 1 has not taken. A request to a certificate
the device has not accepted fails with no warning at all, and iOS can forget an accepted exception
(after a browser restart, say). Open the API URL again and accept.

Record the outcome in the results table below each time the layout, the capture control or the
viewport handling changes.

## Checks

| # | Check | Why it cannot be automated |
| --- | --- | --- |
| 1 | Tapping **Capture a receipt** opens the rear-facing camera directly — not a file browser, not a photo picker — with no screen, prompt or transition first. | iOS grants camera access only to the interaction that requested it; jsdom has no camera and no gesture model. |
| 2 | Taking a photograph lands on `/capture` showing the file's name, and the OS back gesture returns to home. | Requires a real camera and real history behaviour. |
| 3 | Dismissing the camera without taking a photograph returns to home unchanged. | Same. |
| 4 | Scrolling the recent list collapses the browser toolbar, and the capture control stays fully visible and tappable throughout. | `100dvh` resolves against a viewport jsdom does not have. |
| 5 | The capture control clears the home indicator on a device that has one — no part of it sits under the indicator, and it is tappable along its whole width. | `env(safe-area-inset-bottom)` resolves to zero outside a real device. |
| 6 | The header is not cut off by the notch, the dynamic island or the rounded corners, in portrait and in landscape. | `env(safe-area-inset-top/left/right)`, as above. |
| 7 | Nothing scrolls horizontally, including with a purchase whose merchant name is very long. | Depends on real text metrics. |
| 8 | Tapping the capture control does not zoom the page, and text stays legible without pinching. | iOS zooms on focusing a control below 16px; jsdom has no zoom. |

## Devices

Check on at least one of each. Android Chrome is more permissive about the camera gesture than iOS
Safari, so an Android pass is not evidence for check 1.

- iPhone, iOS Safari
- Android phone, Chrome

## Results

Not yet run. Record each run as a row: date, device, OS and browser version, and the outcome of
each numbered check.

| Date | Device | OS / browser | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| | | | | | | | | | | | |
