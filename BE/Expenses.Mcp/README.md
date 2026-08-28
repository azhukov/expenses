# The MCP interface

What an assistant can do with the ledger, tool by tool. This is the *second* front door: a separate
process from the HTTP host, which it never talks to, over the same Application use cases — so a
capability here exists there and vice versa (D1). See [BE/README.md](../README.md) for running the
stack and [CLAUDE.md](CLAUDE.md) for the rules this adapter is held to.

## Server identity

The host advertises itself as `expenses` (`Personal expense ledger`, version `1.0`) and ships
instructions with the handshake, so a client that reads them starts out already knowing the three
things that otherwise cost a failed call each: a purchase is a container of lines whose amounts sum
exactly to its total, reference data is named by **code** and never by display name, and recording
is safe to repeat.

## Two transports

| | | |
| --- | --- | --- |
| **stdio** | the default | `dotnet run --project BE/Expenses.Mcp` — the natural local-assistant experience |
| **HTTP** | `EXPENSES_MCP_TRANSPORT=http` | the same tools over the network; <http://localhost:5083> under compose |

Both register the identical tool set from `ExpensesMcpServer.AddExpensesMcpServer`, and so do the
integration tests — a tool exercised in only one of the three would be a tool nobody has really
exercised. Under stdio nothing but protocol traffic may reach stdout, so the host logs to stderr.
Neither host applies migrations; that is the API's job (D15).

Client configuration for stdio, and the HTTP alternative, are in
[BE/README.md § Connecting an MCP client](../README.md#connecting-an-mcp-client).

## The tools

Seventeen tools in four groups. Every one delegates to a use case and validates nothing itself.

### Purchases

| Tool | Arguments | Returns |
| --- | --- | --- |
| `record_purchase` | `occurredAt`, `amount`, `expenses[]`, `merchant?` | summary sentence, `alreadyRecorded`, `merchantNewlyAdded`, the purchase |
| `get_purchase` | `id` | one purchase with its lines, merchant, discounts and total saving |
| `list_purchases` | `from?`, `to?`, `skip`, `take?` (≤200) | purchases in an inclusive date range, most recent first |

`record_purchase` is **idempotent by natural key**: a purchase whose date, time and amount match one
already recorded is returned as it stands rather than duplicated, and `alreadyRecorded` says so
(D3). That is a success, not an error — an assistant that retries after a timeout gets the original
purchase back, not a second one.

`occurredAt` is local wall-clock time; no timezone is applied. The line amounts must sum **exactly**
to `amount`, or the call fails with `purchase.reconciliation_mismatch`.

An expense line carries `description`, `amount` (what was actually paid — authoritative), `quantity`
(default 1), `unitCode`, `unitPrice` (descriptive: it need not multiply out to the amount),
`categoryCode`, `listUnitPrice` and `discountAmount` (a positive amount, never a percentage — a
percentage is rejected with `expense.discount_percentage_not_accepted`, and a half-specified
discount with `expense.discount_incomplete`).

A merchant is given as `text` — the shop as printed or as the user named it — plus an optional
`taxId`. It is matched against the learned merchant dictionary and added if new; the verbatim text
is kept on the purchase either way (D14, D18).

### Reference data

| Tool | Arguments | Returns |
| --- | --- | --- |
| `list_categories` | `includeInactive` | code, name, parent code, `isSystem`, `isActive` |
| `create_category` | `code`, `name`, `parentCode?` | the new category |
| `rename_category` | `code`, `name` | the renamed category |
| `deactivate_category` | `code` | the retired category |
| `list_units` | `includeInactive` | code, name, symbol, kind (mass / volume / count), `isActive` |

Codes are the contract: `GROCERIES` is unambiguous where "Groceries" is not (D8), and a display name
is not accepted anywhere — an assistant unsure which code to use lists first. A category code is
permanent; the name is not, and expenses keep their assignment across a rename because they
reference the code. Units are fixed, so there is deliberately no `create_unit`
(`unit.creation_not_supported`). A category with active children cannot be retired
(`category.has_active_children`).

### Merchants

| Tool | Arguments | Returns |
| --- | --- | --- |
| `list_merchants` | `includeInactive` | id, name, tax id, parent, `isActive` |
| `search_merchants` | `term` | matches, each with the merchant (or null) and the text that matched |
| `rename_merchant` | `id`, `name` | the renamed merchant |
| `set_merchant_parent` | `id`, `parentId?` | the merchant, re-parented or detached |
| `deactivate_merchant` | `id` | the retired merchant |

Merchants are **learned, not seeded**: one appears the first time a purchase names it. Search
tolerates accents and small misspellings, and also searches the verbatim merchant text kept on
purchases — so a hit can come back with `merchant: null` and a `purchaseId`, which is exactly the
shop that never made it into the dictionary and exactly the one a user searches for (D14).
`set_merchant_parent` records that a branch belongs to a chain, so reporting can roll a branch's
spending up.

### Receipt extraction

| Tool | Arguments | Returns |
| --- | --- | --- |
| `get_extraction` | `purchaseId` | state, stages run, per-check outcomes, fiscal corroboration, `candidatesHeld`, the result |
| `rerun_extraction` | `purchaseId` | the same view, read back after requeueing |
| `confirm_candidates` | `purchaseId`, `expenses?` | the purchase, with the confirmed lines on it |
| `discard_candidates` | `purchaseId` | a sentence confirming the suggestion was withdrawn |

**Receipt images are not uploadable over MCP.** No tool accepts image bytes: uploading is HTTP-only,
and these tools reference images that are already stored. A receipt has no identifier of its own —
it is addressed by its purchase (D11).

Every result opens with a `summary` sentence written to be relayed to the user, naming whatever has
to be acted on: a failed arithmetic check, a fiscal identifier that two sources read differently, or
candidate lines that are no longer held.

State is one of `Pending`, `Extracting`, `Extracted`, `NeedsReview` or `Failed`. `checks` reports
the arithmetic decision by name and outcome, because whether the numbers were read correctly is
decidable rather than estimated (D20) — see
[BE/README.md § Extraction: the cascade](../README.md#extraction-the-cascade).

`candidatesHeld` matters more than it looks. Candidate lines are transient: they live until they are
confirmed, discarded, or the server restarts. So a receipt can report `Extracted` with no lines
beside it, and the flag is what stops an assistant reading that absence as an empty receipt instead
of offering a re-run (D12).

`confirm_candidates` accepts corrected `expenses` — which is both how a misread number is fixed and
how lines are recorded once the candidates are gone. Corrected or not, they must sum exactly to the
purchase amount. `rerun_extraction` replaces unconfirmed candidates and leaves expenses already
confirmed onto the purchase alone; it reads the state back from storage rather than reporting it
from the requeue, because extraction runs off the request path and may not have started yet.

## Errors

A use-case failure becomes a tool error in exactly one place — a call-tool filter in
`ExpensesMcpServer` — so no handler carries error wording of its own and the same failure over HTTP
says the same thing (D1, D22). The text is the message as written, followed by the stable code:

```
The purchase amount is 24.90 while its expenses sum to 23.40. [purchase.reconciliation_mismatch]
```

Codes are part of the contract and are not reworded casually; the full list is
[ApplicationErrors.cs](../Expenses.Application/Errors/ApplicationErrors.cs). The ones an assistant
meets most: `purchase.reconciliation_mismatch`, `purchase.not_found`, `category.not_found`,
`category.inactive`, `category.duplicate_code`, `unit.not_found`, `merchant.not_found`.
