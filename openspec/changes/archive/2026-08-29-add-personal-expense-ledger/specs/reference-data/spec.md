## Purpose

Defines the dictionaries the ledger refers to — categories and units for expense lines, merchants for purchases: how entries are identified, how seeded dictionaries differ from ones learned during ingestion, how they are extended and retired without damaging history, and how text captured verbatim from a receipt relates to them.

## ADDED Requirements

### Requirement: Reference entries are identified by a stable code

Every category and every unit SHALL have a code that is unique, immutable once created, and independent of its display name. Callers, seed data and stored references SHALL identify entries by code. Changing a display name SHALL NOT change a code and SHALL NOT affect existing expenses.

#### Scenario: Code identifies an entry

- **WHEN** a category is referenced by the code GROCERIES
- **THEN** the corresponding category is resolved

#### Scenario: Renaming does not break references

- **WHEN** the display name of the category GROCERIES is changed from "Groceries" to "Food and Drink"
- **THEN** expenses previously assigned to GROCERIES remain assigned to it
- **AND** the code GROCERIES still resolves

#### Scenario: Duplicate code rejected

- **WHEN** a category is created with a code that already exists
- **THEN** the system rejects the request

#### Scenario: Code cannot be changed

- **WHEN** a request attempts to change the code of an existing category
- **THEN** the system rejects the request

### Requirement: Categories are hierarchical

A category MAY have a parent category, forming a tree. The system SHALL reject a parent assignment that would create a cycle. Depth SHALL NOT be limited by the system. A category with children SHALL be assignable to an expense in its own right.

#### Scenario: Child category

- **WHEN** the category PRODUCE is created with parent GROCERIES
- **THEN** PRODUCE is returned as a child of GROCERIES

#### Scenario: Cycle rejected

- **WHEN** a request would make a category a descendant of itself
- **THEN** the system rejects the request

#### Scenario: Parent is assignable

- **WHEN** an expense is assigned the category GROCERIES which has children
- **THEN** the assignment succeeds

### Requirement: Units describe what a quantity counts

Every unit SHALL have a code, a display name, a symbol, and a kind indicating whether it measures mass, volume or discrete count. Units SHALL be fixed reference data and SHALL NOT be created by the user in this change.

#### Scenario: Unit detail

- **WHEN** the unit KG is retrieved
- **THEN** it returns the symbol "kg" and the kind mass

#### Scenario: User cannot create units

- **WHEN** a request attempts to create a new unit
- **THEN** the system rejects the request

### Requirement: Dictionaries are seeded and the seed is repeatable

The system SHALL provide an initial set of categories and units on first run. Seeding SHALL be idempotent: running it again SHALL NOT duplicate entries. Seeding SHALL NOT overwrite or delete entries that the user has modified or created.

#### Scenario: First run

- **WHEN** the system starts against an empty database
- **THEN** the seeded categories and units are present

#### Scenario: Repeated seeding

- **WHEN** seeding runs again against a database that has already been seeded
- **THEN** no entries are duplicated

#### Scenario: User changes survive seeding

- **WHEN** a user has renamed a seeded category and seeding runs again
- **THEN** the renamed display name is retained

#### Scenario: User-created entries survive seeding

- **WHEN** a user has created a category and seeding runs again
- **THEN** that category is retained

### Requirement: Users can create their own categories

A user SHALL be able to create categories in addition to the seeded ones. Seeded entries SHALL be marked as system-provided and SHALL NOT be deleted. User-created categories SHALL behave identically to seeded ones when assigned to expenses.

#### Scenario: Create a category

- **WHEN** a user creates a category with a new code and display name
- **THEN** the category is available for assignment to expenses

#### Scenario: System category cannot be deleted

- **WHEN** a request attempts to delete a system-provided category
- **THEN** the system rejects the request

### Requirement: Entries are retired, not deleted

Deactivating a category or unit SHALL hide it from selection for new expenses while leaving existing expenses that reference it unchanged and still readable. A category with active children SHALL NOT be deactivated until its children are deactivated.

#### Scenario: Deactivated entry is hidden from selection

- **WHEN** the category COMMUTING is deactivated
- **THEN** it is not offered when choosing a category for a new expense

#### Scenario: History is preserved

- **WHEN** the category COMMUTING is deactivated
- **THEN** expenses already assigned to COMMUTING still report that category

#### Scenario: Assigning a deactivated entry is rejected

- **WHEN** a new expense is submitted referencing a deactivated category
- **THEN** the system rejects the request

#### Scenario: Parent with active children

- **WHEN** deactivating a category that has active children is attempted
- **THEN** the system rejects the request and names the blocking children

### Requirement: Expenses may reference reference data loosely

An expense MAY have no category and MAY have no unit. Alongside any reference, an expense MAY retain the verbatim text observed on a receipt. Verbatim text SHALL be retained whether or not a reference was matched, and a later match SHALL NOT erase it.

#### Scenario: Expense without a category

- **WHEN** an expense is recorded with no category
- **THEN** the expense is accepted and reports no category

#### Scenario: Matched reference and verbatim text coexist

- **WHEN** an expense is recorded with the unit KG and the verbatim text "kg."
- **THEN** both are retained and returned

#### Scenario: Matching later does not erase verbatim text

- **WHEN** an expense holding only verbatim unit text is later matched to a unit
- **THEN** the verbatim text is still retained

### Requirement: Dictionaries can be listed

The system SHALL allow listing categories and units. Listings SHALL by default include only active entries, SHALL be able to include inactive entries on request, and SHALL return categories in a defined display order.

#### Scenario: Default listing

- **WHEN** categories are listed without options
- **THEN** only active categories are returned

#### Scenario: Including inactive entries

- **WHEN** categories are listed with inactive entries requested
- **THEN** both active and inactive categories are returned, each marked with its state

#### Scenario: Display order is stable

- **WHEN** categories are listed twice with no intervening change
- **THEN** both listings return the same entries in the same order

### Requirement: Merchants are a dictionary learned during ingestion

The system SHALL maintain a dictionary of merchants that is populated as purchases are recorded rather than seeded. A merchant SHALL have a display name. A merchant SHALL NOT require a stable code, and SHALL NOT be marked as system-provided, because no merchant ships with the system. Recording a purchase naming a merchant that is not yet known SHALL be able to add it.

#### Scenario: First purchase from a new merchant

- **WHEN** a purchase is recorded naming a merchant that is not in the dictionary
- **THEN** the merchant is added to the dictionary
- **AND** the purchase references it

#### Scenario: Second purchase from a known merchant

- **WHEN** a further purchase is recorded naming a merchant already in the dictionary
- **THEN** no second dictionary entry is created
- **AND** both purchases reference the same merchant

#### Scenario: Merchants are not seeded

- **WHEN** the system starts against an empty database
- **THEN** the merchant dictionary is empty
- **AND** the category and unit dictionaries are populated

#### Scenario: A merchant can be renamed

- **WHEN** the display name of a merchant is changed
- **THEN** purchases already referencing that merchant continue to reference it

### Requirement: A merchant is identified by its tax identification number where one exists

A merchant MAY carry a tax identification number. Where present it SHALL be unique across merchants and SHALL be the identity used to decide whether an incoming merchant is one already known. Where absent, the system SHALL fall back to matching by name and SHALL NOT treat a name match as authoritative. A merchant without a tax identification number SHALL be valid.

#### Scenario: Same tax number is the same merchant

- **WHEN** a purchase names a merchant whose tax identification number matches an existing merchant, under a differently spelled name
- **THEN** the existing merchant is used
- **AND** no second entry is created

#### Scenario: Different tax numbers are different merchants

- **WHEN** two merchants share a display name but carry different tax identification numbers
- **THEN** they are kept as two distinct merchants

#### Scenario: Duplicate tax number is rejected

- **WHEN** a merchant is created with a tax identification number that already exists
- **THEN** the system rejects the request

#### Scenario: Merchant without a tax number

- **WHEN** a purchase names a merchant with no tax identification number
- **THEN** the merchant is accepted
- **AND** it is matched by name for subsequent purchases

### Requirement: A merchant may belong to a parent merchant

A merchant MAY have a parent merchant, so that a branch can be recorded beneath the chain it belongs to. The system SHALL reject a parent assignment that would create a cycle. A purchase SHALL be able to reference either a parent or a child. Reporting SHALL be able to roll a child's purchases up to its parent.

#### Scenario: Branch beneath a chain

- **WHEN** the merchant "Aroma 034" is created with parent "AROMA"
- **THEN** it is returned as a child of "AROMA"

#### Scenario: Purchase references a branch

- **WHEN** a purchase references the merchant "Aroma 034"
- **THEN** the purchase reports that merchant
- **AND** a rollup by parent attributes the purchase to "AROMA"

#### Scenario: Purchase references a chain directly

- **WHEN** a purchase references the merchant "AROMA" which has children
- **THEN** the assignment succeeds

#### Scenario: Cycle rejected

- **WHEN** a request would make a merchant a descendant of itself
- **THEN** the system rejects the request

### Requirement: Merchants are retired, not deleted

Deactivating a merchant SHALL hide it from selection for new purchases while leaving existing purchases that reference it unchanged and still readable. A merchant with active children SHALL NOT be deactivated until its children are deactivated.

#### Scenario: Deactivated merchant is hidden from selection

- **WHEN** a merchant is deactivated
- **THEN** it is not offered when choosing a merchant for a new purchase

#### Scenario: History is preserved

- **WHEN** a merchant is deactivated
- **THEN** purchases already referencing it still report that merchant

#### Scenario: Parent with active children

- **WHEN** deactivating a merchant that has active children is attempted
- **THEN** the system rejects the request and names the blocking children

### Requirement: Merchants can be listed and searched

The system SHALL allow listing merchants, by default active entries only and optionally including inactive ones. The system SHALL allow searching merchants by name in a way that tolerates accents and character-level differences, and the search SHALL consider both dictionary names and the verbatim merchant text retained on purchases.

#### Scenario: Default listing

- **WHEN** merchants are listed without options
- **THEN** only active merchants are returned

#### Scenario: Accent-tolerant search

- **WHEN** merchants are searched for a name written without its accents
- **THEN** the accented merchant is returned

#### Scenario: Search finds an unmatched merchant

- **WHEN** a purchase retains verbatim merchant text that matched no dictionary entry
- **AND** a search is made for that text
- **THEN** the purchase is found
