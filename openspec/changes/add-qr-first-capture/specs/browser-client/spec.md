## ADDED Requirements

### Requirement: The fiscal code is read before a photograph is asked for

The capture screen SHALL read the rear camera live and scan every frame for a QR code, and SHALL show
the user what the camera sees so they can bring the code into view. On the first code read, scanning
SHALL stop and the payload SHALL be submitted to the fiscal-capture endpoint without a further tap.
The client SHALL NOT parse or judge the payload itself. Throughout scanning, a control to take a
photograph instead SHALL be available and SHALL open the camera for a photograph as a direct result
of its activation. Where the camera cannot be used live — the browser offers no live camera, the user
refuses permission, or no camera is present — the client SHALL present the photograph control in
place of the viewfinder, without reporting an error.

#### Scenario: A code is read

- **WHEN** a QR code comes into view during scanning
- **THEN** scanning stops
- **AND** the payload is submitted to the fiscal-capture endpoint without a further action
- **AND** the screen indicates that the invoice is being fetched

#### Scenario: The user chooses a photograph

- **WHEN** the user activates the photograph control during scanning
- **THEN** scanning stops and the camera opens for a photograph as a direct result of that activation

#### Scenario: Live camera refused or unavailable

- **WHEN** the capture screen is reached and the live camera is refused, unsupported or absent
- **THEN** the photograph control is presented
- **AND** no error is reported

#### Scenario: Scanning is abandoned

- **WHEN** the user leaves the capture screen during scanning
- **THEN** the camera is released
- **AND** nothing is submitted to the ledger

### Requirement: A photograph is taken only when the code produces no invoice

Where the fiscal-capture endpoint returns Extracted or NeedsReview, the client SHALL present the
result for review and SHALL NOT ask for a photograph. Where it returns Failed, or cannot be reached,
the client SHALL state that the invoice could not be fetched and SHALL present the photograph
control. Where a Failed fiscal capture reported fiscal identifiers, the photograph SHALL be uploaded
together with the payload that was read; where it reported none, the photograph SHALL be uploaded
without it, so that an unrecognised code does not stop the ledger reading the photograph itself.

#### Scenario: The invoice is fetched

- **WHEN** a fiscal capture returns Extracted or NeedsReview
- **THEN** the candidates are presented for review
- **AND** no photograph is asked for

#### Scenario: The invoice is not fetched

- **WHEN** a fiscal capture returns Failed having reported fiscal identifiers
- **THEN** the screen states that the invoice could not be fetched and presents the photograph control
- **AND** the photograph taken is uploaded together with the payload that was read

#### Scenario: The code was not a fiscal code

- **WHEN** a fiscal capture returns Failed having reported no fiscal identifiers
- **THEN** the photograph control is presented
- **AND** the photograph taken is uploaded without the payload

#### Scenario: The fiscal capture cannot be reached

- **WHEN** the fiscal-capture request fails before a response is received
- **THEN** the screen reports that the ledger could not be reached
- **AND** offers to retry the same payload
- **AND** offers the photograph control

### Requirement: An invoice already recorded is flagged at review

Where a capture response names an earlier purchase carrying the same invoice, the review screen SHALL
state that this invoice appears to be recorded already and when that purchase occurred, and SHALL
still let the user confirm.

#### Scenario: A duplicate is flagged

- **WHEN** a capture response names an earlier purchase carrying the same invoice
- **THEN** the review screen states that the invoice appears to be recorded already, with that purchase's date
- **AND** the user can still confirm

#### Scenario: No duplicate

- **WHEN** a capture response names no earlier purchase
- **THEN** the review screen shows no duplicate warning

## MODIFIED Requirements

### Requirement: Capture opens the device camera

Activating the capture action SHALL open the device's rear-facing camera, as a live viewfinder scanning for the receipt's fiscal code, on both iOS and Android browsers. The capture action SHALL be operable by a single direct interaction: the client SHALL NOT require an intervening confirmation or animation between the user's activation and the camera opening. Where the camera is opened for a photograph — from the capture screen's photograph control — it SHALL open as a direct result of that control's own activation, because mobile browsers open the photograph camera only for the interaction that requested it.

#### Scenario: Camera opens on activation

- **WHEN** the capture action is activated on a mobile browser
- **THEN** the device's rear-facing camera opens as a live viewfinder

#### Scenario: No intermediate step precedes the camera

- **WHEN** the capture action is activated
- **THEN** the viewfinder opens as a direct result of that activation
- **AND** no prompt or confirmation from the client is presented first

#### Scenario: The photograph camera opens from its own control

- **WHEN** the photograph control on the capture screen is activated
- **THEN** the camera opens for a photograph as a direct result of that activation

#### Scenario: Capture is abandoned

- **WHEN** the camera is opened and dismissed without reading a code or taking a photograph
- **THEN** the home screen is shown unchanged
- **AND** nothing is submitted to the ledger

### Requirement: A captured image is carried forward unmodified

Where an image is captured, the client SHALL carry its original bytes forward without resizing, re-encoding, cropping or rectifying them. Home SHALL NOT itself read the camera or submit anything to the ledger; it SHALL hand over to the capture screen, which scans for the fiscal code and takes the photograph where one is needed. The client SHALL NOT reject an image on account of its format or size; those judgements belong to the ledger, which reports them in its own error shape.

#### Scenario: Original bytes are preserved

- **WHEN** a photograph is taken through the photograph control
- **THEN** the bytes uploaded are byte-for-byte those the camera produced
- **AND** they have not been resized or re-encoded

#### Scenario: Home does not submit the image

- **WHEN** the capture action on the home screen is activated
- **THEN** nothing is written to the ledger by the home screen
- **AND** the capture screen is shown

#### Scenario: An unusual format is not pre-judged

- **WHEN** a photograph is taken in a format the client cannot itself display
- **THEN** it is carried forward regardless
- **AND** the client does not report it as an error

### Requirement: A captured image is uploaded on arrival

The capture screen SHALL submit a photograph to `POST /receipts/capture` as soon as it is taken, without requiring a further action from the user, together with any fiscal QR payload the client is carrying forward from a fiscal capture that reported identifiers. While the request is outstanding the screen SHALL indicate that extraction is running rather than showing an empty or static screen, since the request does not return until extraction has reached a terminal state.

#### Scenario: Upload starts without a further tap

- **WHEN** a photograph is taken on the capture screen
- **THEN** it is submitted to the capture endpoint immediately
- **AND** no additional action is required to start the upload

#### Scenario: A payload read earlier travels with the photograph

- **WHEN** a photograph is taken after a fiscal capture that reported identifiers but fetched no invoice
- **THEN** the upload carries that payload alongside the image

#### Scenario: The wait is visible

- **WHEN** the capture request is outstanding
- **THEN** the screen indicates that extraction is in progress
- **AND** no candidate lines, amount or date are shown yet

#### Scenario: The upload cannot be reached

- **WHEN** the capture request fails before a response is received
- **THEN** the screen reports that the receipt could not be uploaded
- **AND** offers to retry the same image
- **AND** the original file is not lost by the retry

### Requirement: A failed extraction still allows manual entry

Where an image capture returns Failed, the screen SHALL report the failure reason and SHALL let the user enter the date, amount and expense lines by hand rather than dead-ending; the temporary image remains confirmable with hand-entered data. A failed fiscal capture SHALL instead lead to the photograph control, as the fallback requirement states.

#### Scenario: Failure is explained

- **WHEN** an image capture returns Failed
- **THEN** the screen states the failure reason
- **AND** offers empty fields for the user to enter the date, amount and lines by hand

#### Scenario: A manually entered capture can still be confirmed

- **WHEN** extraction of an image capture failed and the user has entered a date, amount and lines by hand
- **THEN** confirming submits them together with the temporary key, the same as an extracted capture would

### Requirement: Confirming a capture creates the purchase

The review screen SHALL submit the capture's identity — the temporary key of an image capture, or the payload of a fiscal-only capture — the capture's extraction outcome, and the user's final date, amount and expense lines to the purchase-recording endpoint. On success the user SHALL be returned to the home screen with the new purchase visible.

#### Scenario: Confirmation creates a purchase

- **WHEN** the user confirms a capture with a date, an amount and lines that reconcile
- **THEN** a purchase is created with the receipt attached
- **AND** the user is returned to the home screen
- **AND** the new purchase appears among the recent purchases

#### Scenario: Confirming a fiscal-only capture

- **WHEN** the user confirms a capture that came from a fiscal code with no photograph
- **THEN** confirmation submits the payload in place of a temporary key
- **AND** a purchase is created carrying the fiscal identity and no image

#### Scenario: The date defaults from the receipt

- **WHEN** the capture's extraction established an occurred-at date from the receipt and the user has not overridden it
- **THEN** confirmation submits that date without requiring the user to enter one

### Requirement: Recent purchases are listed for recognition

The home screen SHALL list the most recent purchases, most recent first, so that a user can tell at a glance whether a purchase has already been recorded. Each entry SHALL identify the merchant, the amount, when it occurred, and how many expense lines it carries, and SHALL indicate whether it carries a receipt — a receipt image, a fiscal identity, or both. Entries SHALL be presented as information rather than as controls: they SHALL NOT be activatable and SHALL NOT present an affordance suggesting they are.

#### Scenario: Recent purchases are shown

- **WHEN** the home screen is opened and the ledger holds purchases
- **THEN** the most recent purchases are listed, most recent first
- **AND** each shows a merchant, an amount, when it occurred and its number of expense lines

#### Scenario: A purchase carrying a receipt is distinguishable

- **WHEN** a listed purchase has a receipt image attached, a fiscal identity, or both
- **THEN** the entry indicates this
- **AND** an entry for a purchase with neither does not

#### Scenario: Entries are not controls

- **WHEN** an entry in the list is tapped
- **THEN** nothing happens
- **AND** the entry gave no indication that it could be activated

#### Scenario: An empty ledger

- **WHEN** the home screen is opened and no purchases have been recorded
- **THEN** the client states that nothing has been recorded yet
- **AND** the capture action is present
