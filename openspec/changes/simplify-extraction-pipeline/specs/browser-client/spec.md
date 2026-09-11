## MODIFIED Requirements

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
