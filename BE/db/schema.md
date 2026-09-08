# Database schema

PostgreSQL. Generated from `Expenses.Infrastructure/Persistence/Configurations` and
`Migrations/` — currently through `20260902134836_SnakeCaseIdentifiers`. Update this file when a
migration changes the shape.

**Tables and columns are lower snake_case** (D23, reversed from an earlier PascalCase attempt), the
PostgreSQL house style, so `purchases.occurred_at` is the `Purchase.OccurredAt` property with the
casing translated but nothing else. PostgreSQL folds an unquoted identifier to lower case, so this
needs no quoting in hand-written SQL: `select occurred_at from purchases`. Index and constraint
names were already lower snake_case behind their `ix_` / `ck_` / `FK_` prefixes and are unaffected —
nothing writes them by hand except a migration.

Five tables in two zones:

- **Ledger** — `purchases`, `expenses`. What a person confirmed. A purchase carries its receipt
  reference and extraction state inline, in its own columns.
- **Reference data** — `categories`, `units`, `merchants`. Seeded, user-extensible, retired via `is_active` rather than deleted.

There is no ingestion zone. Two things that used to be stored are not:

- **Receipt bytes.** They live in files under a configured receipt store. `purchases` holds the
  reference to the file — hash, key, type, size — and never the content (D11). There is no
  `receipt_images` table: a receipt belongs to exactly one purchase, so it is columns on that row
  rather than a table joined to it.
- **What an engine proposed.** Extraction results and their candidate lines are transient and live
  only in process memory, keyed by purchase (D12).

All primary keys are `bigint` identity columns. All money is `numeric(19,2)`; all quantities are `numeric(12,3)`.

## Entity relationships

```mermaid
erDiagram
    purchases ||--o{ expenses : "line items (cascade)"
    merchants |o--o{ purchases : "merchant_id (restrict)"
    categories |o--o{ expenses : "category_id (restrict)"
    units |o--o{ expenses : "unit_id (restrict)"
    categories |o--o{ categories : "parent_id (restrict)"
    merchants |o--o{ merchants : "parent_id (restrict)"

    purchases {
        bigint id PK "identity"
        timestamp occurred_at "without time zone; unique with amount"
        numeric amount "19,2; unique with occurred_at"
        bigint merchant_id FK "null; restrict"
        text merchant_raw "null; <= 512 chars; gin trigram"
        text receipt_storage_key "null; <= 256 chars; path relative to the receipt store; indexed"
        varchar receipt_content_type "128; null"
        bigint receipt_size_in_bytes "null"
        integer receipt_state "null; 0 Pending, 1 Extracting, 2 Extracted, 3 NeedsReview, 4 Failed"
        text receipt_failure_reason "null"
        text fiscal_ikof_supplied "null; sent by the client at upload"
        text fiscal_ikof_extracted "null; read by the server"
        text fiscal_jikr_supplied "null; sent by the client at upload"
        text fiscal_jikr_extracted "null; read by the server"
        integer fiscal_extracted_source "null; 0 None, 1 SuppliedAtUpload, 2 DecodedFromCode, 3 ReadAsText"
    }

    expenses {
        bigint id PK "identity"
        text description "<= 512 chars; gin trigram"
        numeric quantity "12,3"
        numeric amount "19,2"
        numeric unit_price "19,2; null"
        numeric list_unit_price "19,2; null; recorded with discount_amount or not at all"
        numeric discount_amount "19,2; null; recorded with list_unit_price or not at all"
        bigint category_id FK "null; restrict"
        bigint unit_id FK "null; restrict"
        text category_raw "null; <= 256 chars; what the receipt printed"
        text unit_raw "null; <= 128 chars; what the receipt printed"
        bigint purchase_id FK "null; cascade"
    }

    categories {
        bigint id PK "identity"
        varchar code UK "64; immutable seeding key"
        text name "<= 256 chars; display only"
        bigint parent_id FK "null; restrict; two-level taxonomy"
        boolean is_system "seeded, cannot be deleted"
        boolean is_active
    }

    units {
        bigint id PK "identity"
        varchar code UK "16"
        text name "<= 128 chars"
        varchar symbol "16"
        integer kind "0 Count, 1 Mass, 2 Volume"
        boolean is_active
    }

    merchants {
        bigint id PK "identity"
        text name "<= 256 chars; gin trigram"
        varchar tax_id UK "32; null; unique where not null"
        bigint parent_id FK "null; restrict; chain nests under parent"
        boolean is_active
    }
```

The `receipt_*` and `fiscal_*` columns are all null on a manually entered purchase and are set
together when a receipt is attached — `ck_purchases_receipt_all_or_nothing` is what makes "attached
or not" a single fact rather than six independent nullable ones.

## The receipt file store

The bytes of a receipt live in a file under a configured root directory. The purchase row is the
reference to it.

| Aspect | Rule |
| --- | --- |
| Layout | Content-addressed from the SHA-256: `<root>/<first two hex>/<next two hex>/<full hex><extension>`, and the whole relative path is stored verbatim in `receipt_storage_key` so the layout can change without rewriting history. |
| Extension | Derived from the sniffed content type, never from the uploaded file name. |
| Write order | File first, then the row — an atomic write to a temporary name followed by a rename, then the update. A file no purchase references is inert garbage; a purchase pointing at a file that was never written is the failure that ordering prevents. |
| Deduplication | Content addressing does it: identical bytes resolve to the same path and the second write is a no-op. Two purchases may therefore reference one file, which is why `receipt_storage_key` is **not** unique. |
| Deletion | The reference is cleared first, and the file is deleted afterwards, best effort, **only when no other purchase references the same `receipt_storage_key`**. A shared file outlives the first purchase that referenced it. |
| Backup | The database dump alone is **not** a complete backup. The receipt store must be backed up beside it. |

## Uniqueness

| Index | Guarantees |
| --- | --- |
| `ix_purchases_occurred_at_amount` | The same amount cannot be recorded twice at the same instant — the duplicate-submission guard. |
| `ix_categories_code`, `ix_units_code` | Reference codes are the stable vocabulary, so renaming a name never breaks a reference. |
| `ix_merchants_tax_id` | Unique where `tax_id IS NOT NULL`; merchants without one are unconstrained. |

`ix_purchases_receipt_storage_key` is a plain, non-unique index: it exists so that "does any other
purchase still reference this file" is a lookup rather than a scan at deletion time.

## Check constraints

Each condition is the SQL as stored — no quoting needed, since every identifier is already lower
snake_case.

| Constraint | Condition |
| --- | --- |
| `ck_purchases_receipt_all_or_nothing` | Either every one of `receipt_storage_key`, `receipt_content_type`, `receipt_size_in_bytes` and `receipt_state` is null, or none is |
| `ck_purchases_receipt_storage_key_length` | `receipt_storage_key IS NULL OR length(receipt_storage_key) <= 256` |
| `ck_purchases_merchant_raw_length` | `merchant_raw IS NULL OR length(merchant_raw) <= 512` |
| `ck_expenses_description_length` | `length(description) <= 512` |
| `ck_expenses_category_raw_length` | `category_raw IS NULL OR length(category_raw) <= 256` |
| `ck_expenses_unit_raw_length` | `unit_raw IS NULL OR length(unit_raw) <= 128` |
| `ck_categories_name_length`, `ck_merchants_name_length` | `length(name) <= 256` |
| `ck_units_name_length` | `length(name) <= 128` |

## Fuzzy search

GIN indexes over `pg_trgm`:

| Index | Used for |
| --- | --- |
| `ix_expenses_description_trgm` | Finding a line item by a half-remembered product name. |
| `ix_merchants_name_trgm` | Matching a printed receipt header to a known merchant. |
| `ix_purchases_merchant_raw_trgm` | Searching purchases whose merchant was never resolved. |

## Delete behaviour

Deletes flow one way. Expenses cascade with their purchase, and the receipt reference goes with the
purchase row it lives on — no join, no restrict edge, nothing left behind in the database. Every
reference data link is `RESTRICT`: a category, unit or merchant that history points at cannot be
deleted, only deactivated. The receipt file is dealt with outside the transaction, under the
deletion rule above.
