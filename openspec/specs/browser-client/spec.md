# browser-client Specification

## Purpose

Defines the human front door onto the ledger: a browser client, used primarily one-handed on a phone at the point of purchase, that reads the ledger over the HTTP interface and leads with capturing a receipt rather than with reporting on what was spent.

## Requirements

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

### Requirement: A captured image is uploaded on arrival

The capture screen SHALL submit the image it was handed to `POST /receipts/capture` as soon as the screen is reached, without requiring a further action from the user. While the request is outstanding the screen SHALL indicate that extraction is running rather than showing an empty or static screen, since the request does not return until extraction has reached a terminal state.

#### Scenario: Upload starts without a further tap

- **WHEN** the capture screen receives an image
- **THEN** it is submitted to the capture endpoint immediately
- **AND** no additional action is required to start the upload

#### Scenario: The wait is visible

- **WHEN** the capture request is outstanding
- **THEN** the screen indicates that extraction is in progress
- **AND** no candidate lines, amount or date are shown yet

#### Scenario: The upload cannot be reached

- **WHEN** the capture request fails before a response is received
- **THEN** the screen reports that the receipt could not be uploaded
- **AND** offers to retry the same image
- **AND** the original file is not lost by the retry

### Requirement: A successful capture is presented for review

Where capture returns Extracted or NeedsReview, the screen SHALL present the extraction's candidate expense lines, amount, merchant and occurred-at date, and SHALL let the user edit any of them before confirming. The client SHALL carry the capture's temporary key and reported extraction outcome forward itself, since the server holds neither once the capture response is returned.

#### Scenario: Candidates are shown for review

- **WHEN** capture returns Extracted or NeedsReview
- **THEN** the candidate expense lines, amount, merchant and occurred-at date are shown

#### Scenario: Candidates are editable

- **WHEN** the review screen is showing candidate lines
- **THEN** the user can change the description, amount, quantity or category of any line, add a line, or remove one

#### Scenario: The capture result is not re-requested

- **WHEN** the user edits candidates and later confirms
- **THEN** the temporary key and the extraction outcome sent with confirmation are the ones the original capture response carried, not a freshly re-fetched result

### Requirement: Extraction problems are surfaced before confirmation

Where capture returns NeedsReview, the screen SHALL state why review is needed — an arithmetic
mismatch or a low-confidence value — using the reasons the response reported, rather than a generic
warning. The user SHALL still be able to correct the affected values and confirm.

#### Scenario: An arithmetic mismatch is named

- **WHEN** capture returns NeedsReview because the extracted lines do not sum to the extracted total
- **THEN** the screen states that the amounts did not reconcile
- **AND** the user can edit the lines and still confirm

#### Scenario: A low-confidence value is flagged

- **WHEN** capture returns NeedsReview because a description, merchant name or category guess fell below the confidence threshold
- **THEN** that value is marked on the review screen
- **AND** the user can correct it and still confirm

#### Scenario: An authoritative result that does not reconcile is still shown as read

- **WHEN** capture returns NeedsReview for an invoice the verification service supplied whose amounts did not reconcile
- **THEN** the screen states that the amounts did not reconcile
- **AND** the lines are shown as the service stated them

### Requirement: A failed extraction still allows manual entry

Where capture returns Failed, the screen SHALL report the failure reason and SHALL let the user enter the date, amount and expense lines by hand rather than dead-ending; the temporary image remains confirmable with hand-entered data.

#### Scenario: Failure is explained

- **WHEN** capture returns Failed
- **THEN** the screen states the failure reason
- **AND** offers empty fields for the user to enter the date, amount and lines by hand

#### Scenario: A manually entered capture can still be confirmed

- **WHEN** extraction failed and the user has entered a date, amount and lines by hand
- **THEN** confirming submits them together with the temporary key, the same as an extracted capture would

### Requirement: The review screen refuses an incomplete purchase

The review screen SHALL NOT submit a confirmation while any value it requires is missing or invalid. It SHALL require a date, a total amount and at least one expense line, and on every line a description of at most 200 characters, an amount, a quantity and a unit. The merchant SHALL remain optional, and a line's category SHALL remain optional.

An amount or quantity SHALL be required to be a number; a blank, a non-numeric entry or a negative value SHALL be treated as invalid. Where the receipt itself established the date, that date SHALL satisfy the requirement without the user entering one.

#### Scenario: Confirming with nothing filled in is refused

- **WHEN** the user confirms with no date, no amount and a single empty line
- **THEN** no request is sent to the ledger
- **AND** the screen reports that each of those values is required

#### Scenario: Every offending value is reported at once

- **WHEN** the user confirms with several invalid values across more than one line
- **THEN** every one of them is reported, not only the first

#### Scenario: A purchase with no lines is refused

- **WHEN** the user removes every line and confirms
- **THEN** no request is sent to the ledger
- **AND** the screen states that at least one expense line is required

#### Scenario: An over-long description is refused

- **WHEN** a line's description is longer than 200 characters
- **AND** the user confirms
- **THEN** no request is sent to the ledger
- **AND** that line's description is reported as too long

#### Scenario: A missing unit is refused

- **WHEN** a line has no unit selected
- **AND** the user confirms
- **THEN** no request is sent to the ledger
- **AND** that line's unit is reported as required

#### Scenario: An optional value left empty does not block confirmation

- **WHEN** the merchant is empty and no line has a category
- **AND** every required value is present and valid
- **THEN** the confirmation is submitted

#### Scenario: A date from the receipt satisfies the requirement

- **WHEN** the capture established an occurred-at date from the receipt and the user has not changed it
- **THEN** the date is not reported as missing
- **AND** confirmation is submitted

### Requirement: Invalid values are marked on the inputs at fault

Where the review screen refuses a confirmation, it SHALL mark each input whose value is at fault, distinguishably from a valid input, and SHALL state beside each what is wrong with it, rather than reporting the problems only as a list away from the fields. A mark SHALL be removed as soon as that input's value becomes valid, without requiring another attempt to confirm. Marking SHALL be conveyed by more than colour alone, and a marked input SHALL be announced as invalid to assistive technology together with its message.

#### Scenario: The faulty input is marked

- **WHEN** confirmation is refused because a line's amount is blank
- **THEN** that line's amount input is marked as invalid and carries a message saying so
- **AND** the inputs whose values are valid are not marked

#### Scenario: A correction clears the mark

- **WHEN** a marked input is given a valid value
- **THEN** its mark and message are removed without the user confirming again

#### Scenario: A marked input is announced as invalid

- **WHEN** an input is marked as invalid
- **THEN** it is exposed to assistive technology as invalid, with its message associated to it

### Requirement: The review screen names the extraction engine

The review screen SHALL show, above the values being reviewed, the name of the extraction engine the capture response reported, so the user knows which reading they are correcting. Where a capture produced no extraction result, the screen SHALL say that no engine reading is available rather than showing an empty or invented name.

#### Scenario: The engine that produced the reading is named

- **WHEN** capture returns a result reporting an engine name
- **THEN** that name is shown at the top of the review screen

#### Scenario: A failed capture states that there is no reading

- **WHEN** capture returns Failed and carries no extraction result
- **THEN** the screen states that no extraction engine reading is available
- **AND** shows no engine name

### Requirement: Confirming a capture creates the purchase

The review screen SHALL submit the temporary key, the capture's extraction outcome, and the user's final date, amount and expense lines to the purchase-recording endpoint. On success the user SHALL be returned to the home screen with the new purchase visible.

#### Scenario: Confirmation creates a purchase

- **WHEN** the user confirms a capture with a date, an amount and lines that reconcile
- **THEN** a purchase is created with the receipt attached
- **AND** the user is returned to the home screen
- **AND** the new purchase appears among the recent purchases

#### Scenario: The date defaults from the receipt

- **WHEN** the capture's extraction established an occurred-at date from the receipt and the user has not overridden it
- **THEN** confirmation submits that date without requiring the user to enter one

### Requirement: A rejected confirmation is reported in place

Where confirmation is rejected by the ledger — the lines do not reconcile with the amount, or a value the client could not judge is refused — the review screen SHALL report the specific reason and SHALL leave the user's entered values and the capture in place so they can correct and resubmit, rather than returning to the home screen or discarding what was entered. Rejections the client can determine for itself SHALL be reported before submission instead, on the inputs at fault.

#### Scenario: A reconciliation mismatch is reported

- **WHEN** confirmation is submitted with lines that do not sum to the entered amount
- **THEN** the screen reports the mismatch
- **AND** the entered lines, amount and date remain on the screen for correction

#### Scenario: A missing date never reaches the ledger

- **WHEN** the user confirms with no date entered and the receipt carried no fiscal timestamp
- **THEN** no request is sent to the ledger
- **AND** the date input is marked, stating that a date is required
- **AND** nothing else the user entered is lost

### Requirement: Abandoning a capture leaves no trace

Where the user leaves the capture screen without confirming, the client SHALL NOT attempt to explicitly delete the temporary capture; an unconfirmed capture is removed automatically by the server's own daily cleanup. Returning to the home screen after abandoning SHALL show the ledger exactly as it was before the capture.

#### Scenario: Leaving without confirming changes nothing

- **WHEN** the user navigates away from the capture screen without confirming
- **THEN** no purchase is created
- **AND** the home screen shows the ledger unchanged

### Requirement: The ledger address is configured when the client is served

The client SHALL reach the ledger at an address taken from the environment of the server that serves the client, read when that server starts. The address SHALL NOT be fixed into the built client: serving the same build with a different address in the environment SHALL send every request to the new address without rebuilding. Every read, write and upload SHALL be sent to that address, and none SHALL rely on the server that serves the client forwarding requests to the ledger. Where the address is absent, or is not an absolute `http` or `https` URL, the serving server SHALL refuse to start and SHALL name the missing or malformed setting, rather than serving a client that cannot reach the ledger. An address given with or without a trailing slash SHALL produce the same request URLs.

#### Scenario: Requests go to the configured address

- **WHEN** the client is served with the ledger address set to `http://ledger.test:9000`
- **AND** the home screen loads purchases
- **THEN** the request is sent to `http://ledger.test:9000/purchases` with the month's query
- **AND** no request for ledger data is sent to the origin that served the client

#### Scenario: Uploads go to the configured address

- **WHEN** a captured image is uploaded
- **THEN** the upload is sent to the capture endpoint under the configured address

#### Scenario: A different address needs no rebuild

- **WHEN** a built client is served once with one ledger address and again with another
- **THEN** each time its requests go to the address the server was started with

#### Scenario: A trailing slash does not change the requests

- **WHEN** the address is given as `http://ledger.test:9000/`
- **THEN** a request for merchants is sent to `http://ledger.test:9000/merchants`, with no doubled slash

#### Scenario: A missing address stops the server

- **WHEN** the development or preview server is started with no ledger address in its environment
- **THEN** it does not start
- **AND** its output names the setting that is missing

#### Scenario: A malformed address stops the server

- **WHEN** the server is started with a ledger address that is not an absolute `http` or `https` URL
- **THEN** it does not start
- **AND** its output names the setting and says why the value was refused

#### Scenario: Ledger errors are still shown cross-origin

- **WHEN** the client is served from a different origin than the ledger's
- **AND** a request fails with an error carrying a human-readable message
- **THEN** that message is what the user is shown, not a report that the ledger could not be reached
