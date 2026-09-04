## ADDED Requirements

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

Where capture returns NeedsReview, the screen SHALL state why review is needed — an arithmetic mismatch, a low-confidence value, or disagreeing fiscal identifiers — using the reasons the response reported, rather than a generic warning. The user SHALL still be able to correct the affected values and confirm.

#### Scenario: An arithmetic mismatch is named

- **WHEN** capture returns NeedsReview because the extracted lines do not sum to the extracted total
- **THEN** the screen states that the amounts did not reconcile
- **AND** the user can edit the lines and still confirm

#### Scenario: A low-confidence value is flagged

- **WHEN** capture returns NeedsReview because a description, merchant name or category guess fell below the confidence threshold
- **THEN** that value is marked on the review screen
- **AND** the user can correct it and still confirm

#### Scenario: Disagreeing fiscal identifiers are shown

- **WHEN** capture reports that a fiscal identifier supplied at capture disagrees with the one extraction read from the image
- **THEN** the screen states the disagreement
- **AND** confirmation is not blocked by it

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

Where the user leaves the capture screen without confirming, the client SHALL NOT attempt to explicitly delete the temporary capture; an unconfirmed capture is removed automatically by the server's own daily cleanup. Returning to the home screen after abandoning SHALL show the ledger exactly as it was before the capture.

#### Scenario: Leaving without confirming changes nothing

- **WHEN** the user navigates away from the capture screen without confirming
- **THEN** no purchase is created
- **AND** the home screen shows the ledger unchanged
