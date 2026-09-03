## Purpose

Defines the human front door onto the ledger: a browser client, used primarily one-handed on a phone at the point of purchase, that reads the ledger over the HTTP interface and leads with capturing a receipt rather than with reporting on what was spent.

## ADDED Requirements

### Requirement: The client leads with capture

The home screen SHALL present capturing a receipt as its primary action, reachable without scrolling and without any prior navigation. No other control on the screen SHALL be given equal or greater prominence. The capture action SHALL remain available regardless of whether ledger data could be read, because capturing does not depend on it.

#### Scenario: Capture is reachable on arrival

- **WHEN** the home screen is opened
- **THEN** the capture action is visible without scrolling
- **AND** it is the most prominent control on the screen

#### Scenario: Capture stays reachable while browsing recent purchases

- **WHEN** the list of recent purchases is scrolled
- **THEN** the capture action remains visible and usable

#### Scenario: Capture survives a failure to read the ledger

- **WHEN** the home screen is opened and the ledger cannot be read
- **THEN** the capture action is still present and usable

### Requirement: Capture opens the device camera

Activating the capture action SHALL open the device's rear-facing camera directly, rather than a file browser, on both iOS and Android browsers. The capture action SHALL be operable by a single direct interaction: the client SHALL NOT require an intervening navigation, confirmation or animation between the user's activation and the camera opening, because mobile browsers grant camera access only to the interaction that requested it.

#### Scenario: Camera opens on activation

- **WHEN** the capture action is activated on a mobile browser
- **THEN** the device's rear-facing camera opens

#### Scenario: No intermediate step precedes the camera

- **WHEN** the capture action is activated
- **THEN** the camera opens as a direct result of that activation
- **AND** no screen, prompt or transition is presented first

#### Scenario: Capture is abandoned

- **WHEN** the camera is opened and dismissed without taking a photograph
- **THEN** the home screen is shown unchanged
- **AND** nothing is submitted to the ledger

### Requirement: A captured image is carried forward unmodified

Where an image is captured, the client SHALL carry its original bytes forward without resizing, re-encoding, cropping or rectifying them. Home SHALL NOT itself submit the image to the ledger; it SHALL hand the captured image to the screen that performs capture. The client SHALL NOT reject an image on account of its format or size; those judgements belong to the ledger, which reports them in its own error shape.

#### Scenario: Original bytes are preserved

- **WHEN** a photograph is taken through the capture action
- **THEN** the bytes handed onward are byte-for-byte those the camera produced
- **AND** they have not been resized or re-encoded

#### Scenario: Home does not submit the image

- **WHEN** a photograph is taken through the capture action
- **THEN** nothing is written to the ledger by the home screen
- **AND** the image is handed to the capture screen

#### Scenario: An unusual format is not pre-judged

- **WHEN** a photograph is taken in a format the client cannot itself display
- **THEN** it is carried forward regardless
- **AND** the client does not report it as an error

### Requirement: Recent purchases are listed for recognition

The home screen SHALL list the most recent purchases, most recent first, so that a user can tell at a glance whether a purchase has already been recorded. Each entry SHALL identify the merchant, the amount, when it occurred, and how many expense lines it carries, and SHALL indicate whether a receipt image is attached. Entries SHALL be presented as information rather than as controls: they SHALL NOT be activatable and SHALL NOT present an affordance suggesting they are.

#### Scenario: Recent purchases are shown

- **WHEN** the home screen is opened and the ledger holds purchases
- **THEN** the most recent purchases are listed, most recent first
- **AND** each shows a merchant, an amount, when it occurred and its number of expense lines

#### Scenario: A purchase carrying a receipt is distinguishable

- **WHEN** a listed purchase has a receipt image attached
- **THEN** the entry indicates this
- **AND** an entry for a purchase with no receipt does not

#### Scenario: Entries are not controls

- **WHEN** an entry in the list is tapped
- **THEN** nothing happens
- **AND** the entry gave no indication that it could be activated

#### Scenario: An empty ledger

- **WHEN** the home screen is opened and no purchases have been recorded
- **THEN** the client states that nothing has been recorded yet
- **AND** the capture action is present

### Requirement: Display names are resolved from reference data

The ledger records a merchant on a purchase as an identifier alongside the text the receipt printed, and does not carry the merchant's display name with it. The client SHALL resolve the display name from the merchant dictionary. Where a purchase references no merchant, or references one the dictionary does not describe, the client SHALL fall back to the verbatim receipt text, and where there is none either, SHALL say that the merchant is unknown. The client SHALL NOT present a bare identifier to the user, and SHALL NOT suppress a purchase because its merchant could not be named.

#### Scenario: A known merchant is named

- **WHEN** a purchase references a merchant that the dictionary describes
- **THEN** the entry shows that merchant's display name

#### Scenario: An unmatched merchant falls back to what was printed

- **WHEN** a purchase carries verbatim merchant text but references no merchant
- **THEN** the entry shows the verbatim text

#### Scenario: Neither a merchant nor verbatim text

- **WHEN** a purchase carries neither a merchant reference nor verbatim text
- **THEN** the entry states that the merchant is unknown
- **AND** the purchase is still listed

#### Scenario: Identifiers are never displayed

- **WHEN** any purchase is listed
- **THEN** no merchant or category identifier appears in what is shown

#### Scenario: Reference data cannot be read

- **WHEN** the merchant dictionary cannot be read but purchases can
- **THEN** the purchases are still listed using their verbatim merchant text
- **AND** the failure is not reported as a failure to load the ledger

### Requirement: The month to date is reported as a single figure

The home screen SHALL report the total amount of purchases occurring in the current calendar month to date. It SHALL report that figure alone: the home screen SHALL NOT present a breakdown by category, a comparison against another period, or a chart of any kind. Where no purchases have occurred in the current month, the figure SHALL be reported as zero rather than omitted.

#### Scenario: The total reflects the current month

- **WHEN** the home screen is opened
- **THEN** the sum of the amounts of purchases occurring in the current calendar month is shown
- **AND** purchases occurring before the current month are not counted

#### Scenario: No purchases this month

- **WHEN** the current calendar month holds no purchases
- **THEN** the total is shown as zero

#### Scenario: No breakdown accompanies the total

- **WHEN** the home screen is opened
- **THEN** no category breakdown, period comparison or chart is shown

### Requirement: Receipts needing review are surfaced

Where the ledger holds purchases whose receipt is in the review-needed extraction state, the home screen SHALL report how many, so that unfinished work is not silently forgotten. Where there are none, the client SHALL show nothing in its place rather than a zero.

#### Scenario: Purchases needing review exist

- **WHEN** the current month holds purchases whose receipts need review
- **THEN** the home screen reports how many there are

#### Scenario: Nothing needs review

- **WHEN** no purchase in the current month has a receipt needing review
- **THEN** no such report appears on the screen

### Requirement: Failure to read the ledger degrades rather than breaks

Where ledger data cannot be read, the client SHALL report that it could not be read, SHALL offer to try again, and SHALL leave the rest of the screen functional. The client SHALL surface the message the ledger's own error carries where it has one, and SHALL NOT invent a second vocabulary of error codes or replace a specific message with a generic one. While a read is in progress the client SHALL indicate that it is loading rather than presenting an empty screen.

#### Scenario: The ledger cannot be reached

- **WHEN** the request for purchases fails
- **THEN** the client reports that purchases could not be loaded
- **AND** offers to retry
- **AND** the capture action remains usable

#### Scenario: Retrying succeeds

- **WHEN** a failed read is retried and succeeds
- **THEN** the purchases are shown
- **AND** the failure report is no longer present

#### Scenario: The ledger reports a specific error

- **WHEN** a request fails with an error carrying a human-readable message
- **THEN** that message is what the user is shown

#### Scenario: Loading is visible

- **WHEN** the home screen is opened and purchases have not yet arrived
- **THEN** the client indicates that it is loading

#### Scenario: An empty ledger is not a failure

- **WHEN** the request for purchases succeeds and returns none
- **THEN** the empty state is shown rather than an error

### Requirement: The client fits the device it is used on

The client SHALL be usable on phone, tablet and desktop viewports without horizontal scrolling. On a phone, content SHALL NOT be obscured by the browser's own interface or by the device's rounded corners, notch or home indicator, and controls anchored to the bottom of the screen SHALL remain fully visible and tappable as the browser interface expands and collapses. Every interactive control SHALL present a touch target of at least 44 by 44 CSS pixels. Text SHALL remain legible without the browser zooming.

#### Scenario: No horizontal scrolling

- **WHEN** the home screen is viewed at any viewport width from a small phone upward
- **THEN** the page does not scroll horizontally

#### Scenario: The bottom of the screen is not obscured

- **WHEN** the home screen is viewed on a phone with a home indicator
- **THEN** the capture action is fully visible above it
- **AND** it is fully tappable

#### Scenario: The browser interface collapses

- **WHEN** the page is scrolled such that the browser's toolbar collapses
- **THEN** the capture action remains fully visible
- **AND** no content is cut off at the bottom of the viewport

#### Scenario: Touch targets are large enough

- **WHEN** any interactive control on the home screen is measured
- **THEN** its touch target is at least 44 by 44 CSS pixels

### Requirement: Amounts and dates are formatted for reading

The client SHALL present monetary amounts in euro with two decimal places, and SHALL present dates in a form a reader recognises rather than as a machine timestamp. Dates occurring today or yesterday SHALL be described in those terms rather than by their date. The client SHALL NOT alter, round or recompute an amount it was given; formatting SHALL affect presentation only.

#### Scenario: An amount is formatted

- **WHEN** a purchase amount is displayed
- **THEN** it is shown in euro with two decimal places

#### Scenario: A recent date is described in relative terms

- **WHEN** a purchase occurred today
- **THEN** its entry describes it as today rather than by its date

#### Scenario: An older date is shown as a date

- **WHEN** a purchase occurred more than one day ago
- **THEN** its entry shows the date

#### Scenario: Formatting does not change values

- **WHEN** an amount is displayed and the same purchase is read from the ledger
- **THEN** the displayed value equals the stored value
