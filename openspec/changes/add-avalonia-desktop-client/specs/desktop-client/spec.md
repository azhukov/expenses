## Purpose

Defines the desktop front door onto the ledger: a windowed client for Windows, macOS and Linux that reads and writes the ledger over the HTTP interface, leads with capturing a receipt from an image file, and offers the same launcher and capture-review-confirm flow as the browser client wherever a desktop does not change the behaviour.

## ADDED Requirements

### Requirement: The client reaches the ledger at a configured address

The client SHALL address the ledger's HTTP interface at a base address supplied by configuration, defaulting to the address the local stack publishes. The client SHALL NOT embed any other address, and SHALL NOT depend on being served from the same origin as the interface.

#### Scenario: The default address reaches the local stack

- **WHEN** the client is started with no address configured and the local stack is running
- **THEN** it reads the ledger from the interface the local stack publishes

#### Scenario: A configured address is used

- **WHEN** the client is started with a base address configured
- **THEN** every request it makes is sent to that address

#### Scenario: A misconfigured address degrades rather than breaks

- **WHEN** the configured address cannot be reached
- **THEN** the home screen reports that purchases could not be loaded and offers to retry
- **AND** the client does not exit or show an unhandled error

### Requirement: The client leads with capture

The home screen SHALL present capturing a receipt as its primary action, visible without scrolling and without any prior navigation. No other control on the screen SHALL be given equal or greater prominence. The capture action SHALL remain available regardless of whether ledger data could be read, because capturing does not depend on it.

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

### Requirement: Capture takes an image from a file or a drop

Activating the capture action SHALL open the operating system's file picker, offering image files by default without preventing any other file from being chosen. The home screen SHALL also accept a single file dropped onto the window as a capture. Choosing or dropping exactly one file SHALL hand it to the capture screen with no further confirmation. Dismissing the picker, or dropping something that is not a file, SHALL leave the home screen unchanged. Dropping more than one file at once SHALL NOT capture any of them, and SHALL state that receipts are captured one at a time.

#### Scenario: Choosing a file captures it

- **WHEN** the capture action is activated and an image file is chosen in the picker
- **THEN** the capture screen is shown for that file
- **AND** no further confirmation is asked for

#### Scenario: Dropping a file captures it

- **WHEN** a single image file is dropped onto the home screen
- **THEN** the capture screen is shown for that file

#### Scenario: Capture is abandoned

- **WHEN** the file picker is opened and dismissed without choosing a file
- **THEN** the home screen is shown unchanged
- **AND** nothing is submitted to the ledger

#### Scenario: Several files are dropped at once

- **WHEN** more than one file is dropped onto the home screen together
- **THEN** none of them is captured
- **AND** the screen states that receipts are captured one at a time

#### Scenario: Something other than a file is dropped

- **WHEN** text or another non-file item is dropped onto the home screen
- **THEN** the home screen is shown unchanged
- **AND** nothing is submitted to the ledger

### Requirement: A captured image is carried forward unmodified

The client SHALL carry the chosen file's original bytes forward without resizing, re-encoding, cropping or rectifying them. Home SHALL NOT itself submit the image to the ledger; it SHALL hand the image to the screen that performs capture. The client SHALL NOT reject a file on account of its format or size; those judgements belong to the ledger, which reports them in its own error shape.

#### Scenario: Original bytes are preserved

- **WHEN** a file is chosen or dropped for capture
- **THEN** the bytes submitted to the ledger are byte-for-byte those of the file

#### Scenario: Home does not submit the image

- **WHEN** a file is chosen or dropped for capture
- **THEN** nothing is written to the ledger by the home screen
- **AND** the image is handed to the capture screen

#### Scenario: An unusual format is not pre-judged

- **WHEN** a file in a format the client cannot itself display is chosen
- **THEN** it is carried forward regardless
- **AND** the client does not report it as an error

#### Scenario: The ledger's judgement of a file is shown

- **WHEN** the ledger rejects a submitted file with an error carrying a human-readable message
- **THEN** that message is what the user is shown

### Requirement: Recent purchases are listed for recognition

The home screen SHALL list the most recent purchases, most recent first, so that a user can tell at a glance whether a purchase has already been recorded. Each entry SHALL identify the merchant, the amount, when it occurred, and how many expense lines it carries, and SHALL indicate whether a receipt image is attached. Entries SHALL be presented as information rather than as controls: activating one SHALL do nothing, and entries SHALL NOT present a hover, selection or focus affordance suggesting they can be activated.

#### Scenario: Recent purchases are shown

- **WHEN** the home screen is opened and the ledger holds purchases
- **THEN** the most recent purchases are listed, most recent first
- **AND** each shows a merchant, an amount, when it occurred and its number of expense lines

#### Scenario: A purchase carrying a receipt is distinguishable

- **WHEN** a listed purchase has a receipt image attached
- **THEN** the entry indicates this
- **AND** an entry for a purchase with no receipt does not

#### Scenario: Entries are not controls

- **WHEN** an entry in the list is clicked, double-clicked or reached by the keyboard
- **THEN** nothing happens
- **AND** the entry gave no indication that it could be activated

#### Scenario: An empty ledger

- **WHEN** the home screen is opened and no purchases have been recorded
- **THEN** the client states that nothing has been recorded yet
- **AND** the capture action is present

### Requirement: Display names are resolved from reference data

The client SHALL resolve a purchase's merchant display name from the merchant dictionary. Where a purchase references no merchant, or references one the dictionary does not describe, the client SHALL fall back to the verbatim receipt text, and where there is none either, SHALL say that the merchant is unknown. The client SHALL NOT present a bare identifier to the user, and SHALL NOT suppress a purchase because its merchant could not be named.

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

The home screen SHALL report the total amount of purchases occurring in the current calendar month to date, by the computer's local calendar. It SHALL report that figure alone: the home screen SHALL NOT present a breakdown by category, a comparison against another period, or a chart of any kind. Where no purchases have occurred in the current month, the figure SHALL be reported as zero rather than omitted.

#### Scenario: The total reflects the current month

- **WHEN** the home screen is opened
- **THEN** the sum of the amounts of purchases occurring in the current calendar month is shown
- **AND** purchases occurring before the current month are not counted

#### Scenario: A purchase late on the last day of the month is counted

- **WHEN** a purchase occurred late in the evening of the current month's last day, in wall-clock time
- **THEN** it is counted in that month's total

#### Scenario: No purchases this month

- **WHEN** the current calendar month holds no purchases
- **THEN** the total is shown as zero

#### Scenario: No breakdown accompanies the total

- **WHEN** the home screen is opened
- **THEN** no category breakdown, period comparison or chart is shown

### Requirement: Receipts needing review are surfaced

Where the current month holds purchases whose receipt is in the review-needed extraction state, the home screen SHALL report how many, so that unfinished work is not silently forgotten. Where there are none, the client SHALL show nothing in its place rather than a zero.

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

### Requirement: The client fits the window it is shown in

The client SHALL be usable at every window size from its minimum upward, without horizontal scrolling and without content being cut off. The window SHALL NOT be resizable below a minimum at which the capture action, the month's total and the start of the recent purchases are all visible. Every interactive control SHALL be reachable and operable from the keyboard alone, SHALL show visibly when it has keyboard focus, and SHALL expose a name to assistive technology. Text SHALL follow the operating system's text scaling.

#### Scenario: No horizontal scrolling

- **WHEN** the home or review screen is shown at any window width from the minimum upward
- **THEN** the screen does not scroll horizontally

#### Scenario: The minimum window still leads with capture

- **WHEN** the window is resized to its minimum size
- **THEN** the capture action and the month's total are fully visible

#### Scenario: The keyboard reaches everything

- **WHEN** the user moves through the home or review screen with the keyboard alone
- **THEN** every interactive control can be reached and activated
- **AND** the control holding focus is visibly marked

#### Scenario: Controls are named

- **WHEN** any interactive control is inspected by assistive technology
- **THEN** it exposes a name describing what it does

### Requirement: Amounts and dates are formatted for reading

The client SHALL present monetary amounts in euro with two decimal places, and SHALL present dates in a form a reader recognises rather than as a machine timestamp. Dates occurring today or yesterday SHALL be described in those terms rather than by their date. The ledger's timestamps are wall-clock time and SHALL be read as such, not converted through a time zone. The client SHALL NOT alter, round or recompute an amount it was given; formatting SHALL affect presentation only.

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

The capture screen SHALL submit the image it was handed to `POST /receipts/capture` as soon as the screen is reached, without requiring a further action from the user, and SHALL submit it once per capture. While the request is outstanding the screen SHALL indicate that extraction is running rather than showing an empty or static screen, since the request does not return until extraction has reached a terminal state.

#### Scenario: Upload starts without a further action

- **WHEN** the capture screen receives an image
- **THEN** it is submitted to the capture endpoint immediately
- **AND** no additional action is required to start the upload

#### Scenario: One capture is one upload

- **WHEN** the capture screen receives an image and is shown, redrawn or resized
- **THEN** the image is submitted to the capture endpoint exactly once

#### Scenario: The wait is visible

- **WHEN** the capture request is outstanding
- **THEN** the screen indicates that extraction is in progress
- **AND** no candidate lines, amount or date are shown yet

#### Scenario: The upload cannot be reached

- **WHEN** the capture request fails before a response is received
- **THEN** the screen reports that the receipt could not be uploaded
- **AND** offers to retry the same image
- **AND** a retry submits the same bytes that were originally chosen

### Requirement: A successful capture is presented for review

Where capture returns Extracted or NeedsReview, the screen SHALL present the extraction's candidate expense lines, amount, merchant and occurred-at date, and SHALL let the user edit any of them before confirming. The client SHALL carry the capture's temporary key and reported extraction outcome forward itself, since the server holds neither once the capture response is returned.

#### Scenario: Candidates are shown for review

- **WHEN** capture returns Extracted or NeedsReview
- **THEN** the candidate expense lines, amount, merchant and occurred-at date are shown

#### Scenario: Candidates are editable

- **WHEN** the review screen is showing candidate lines
- **THEN** the user can change the description, amount, quantity, category or unit of any line, add a line, or remove one

#### Scenario: A partly typed number is not lost

- **WHEN** the user is part-way through typing a number that is not yet valid in an amount or quantity field
- **THEN** what they have typed stays in the field as typed

#### Scenario: The capture result is not re-requested

- **WHEN** the user edits candidates and later confirms
- **THEN** the temporary key and the extraction outcome sent with confirmation are the ones the original capture response carried, not a freshly re-fetched result

### Requirement: Extraction problems are surfaced before confirmation

Where capture returns NeedsReview, the screen SHALL state why review is needed — an arithmetic mismatch or a low-confidence value — using the reasons the response reported, rather than a generic warning. A low-confidence value SHALL be marked on the line it belongs to. The user SHALL still be able to correct the affected values and confirm.

#### Scenario: An arithmetic mismatch is named

- **WHEN** capture returns NeedsReview because the extracted lines do not sum to the extracted total
- **THEN** the screen states that the amounts did not reconcile
- **AND** the user can edit the lines and still confirm

#### Scenario: A low-confidence value is flagged

- **WHEN** capture returns NeedsReview because a description, merchant name or category guess fell below the confidence threshold
- **THEN** that value is marked on the line it belongs to
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

### Requirement: Confirming a capture creates the purchase

The review screen SHALL submit the temporary key, the capture's extraction outcome, and the user's final date, amount and expense lines to the purchase-recording endpoint. While the submission is outstanding the confirm action SHALL NOT be activatable a second time. On success the user SHALL be returned to the home screen, which SHALL read the ledger again so that the new purchase is visible.

#### Scenario: Confirmation creates a purchase

- **WHEN** the user confirms a capture with a date, an amount and lines that reconcile
- **THEN** a purchase is created with the receipt attached
- **AND** the user is returned to the home screen
- **AND** the new purchase appears among the recent purchases
- **AND** the month's total includes it

#### Scenario: Confirming cannot be repeated while outstanding

- **WHEN** a confirmation has been submitted and has not yet been answered
- **THEN** the confirm action cannot be activated again

#### Scenario: The date defaults from the receipt

- **WHEN** the capture's extraction established an occurred-at date from the receipt and the user has not overridden it
- **THEN** confirmation submits that date without requiring the user to enter one

### Requirement: A rejected confirmation is reported in place

Where confirmation is rejected — the lines do not reconcile with the amount, or no date is available from either the receipt or the user — the review screen SHALL report the specific reason and SHALL leave the user's entered values and the capture in place so they can correct and resubmit, rather than returning to the home screen or discarding what was entered.

#### Scenario: A reconciliation mismatch is reported

- **WHEN** confirmation is submitted with lines that do not sum to the entered amount
- **THEN** the screen reports the mismatch
- **AND** the entered lines, amount and date remain on the screen for correction

#### Scenario: A missing date is reported

- **WHEN** confirmation is submitted with no date entered and the receipt carried no fiscal timestamp
- **THEN** the screen states that a date is required
- **AND** nothing else the user entered is lost

### Requirement: Abandoning a capture leaves no trace

The capture and review screens SHALL offer a way back to the home screen without confirming. Where the user leaves without confirming, the client SHALL NOT attempt to explicitly delete the temporary capture; an unconfirmed capture is removed automatically by the server's own daily cleanup. Returning to the home screen after abandoning SHALL show the ledger exactly as it was before the capture.

#### Scenario: Leaving without confirming changes nothing

- **WHEN** the user goes back from the capture or review screen without confirming
- **THEN** no purchase is created
- **AND** no request other than the original capture has been made
- **AND** the home screen shows the ledger unchanged

#### Scenario: Leaving while extraction is running

- **WHEN** the user goes back while the capture request is still outstanding
- **THEN** the home screen is shown
- **AND** a response arriving afterwards does not open the review screen
