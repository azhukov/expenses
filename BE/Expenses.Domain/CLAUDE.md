# Expenses.Domain — the innermost ring

Zero dependencies: no `ProjectReference`, no `PackageReference`, no EF, no `Microsoft.Extensions.*`,
no `IServiceCollection`, no `async`. A test asserts this. Everything here is pure, synchronous and
in-memory.

## Entities and nothing else (D22)

**Every type in this project is an entity, or a value one owns.** Eight of them:

| Type | Role |
| --- | --- |
| [Purchase.cs](Purchase.cs) | The **only aggregate root** (D2). Owns its expenses and the reconciliation invariant. |
| [Expense.cs](Expense.cs) | A line inside a purchase. No repository, no independent lifecycle. |
| [Category.cs](Category.cs), [Unit.cs](Unit.cs) | Dictionary entries keyed by immutable `Code` (D8). |
| [Merchant.cs](Merchant.cs) | Learned dictionary, keyed by tax id, hierarchical (D18). |
| [Receipt.cs](Receipt.cs) | A value **inside** `Purchase`: the reference to its stored image file. Never the bytes, and never a row of its own (D11). |
| [FiscalInvoice.cs](FiscalInvoice.cs) | A value **inside** `Purchase`, independent of the image: the fiscal QR payload and the identifiers read from it or answered for it (D35). |
| [Extraction/ExtractionResult.cs](Extraction/ExtractionResult.cs) | `ExtractionResult` and `ExtractionCandidate` — unconfirmed suggestions, held apart from the aggregate (D12). |

Nothing else is added here. Not a value object, not a wrapper over a single primitive (`Money`,
`Quantity`, `Percentage`, `TaxId`), not a validator, not an exception type, not an error-code table,
not an interface, not a service, not a record, not a mapper, not a domain event. Each of those was
tried and removed; D22 lists what moved where. The rule takes no judgement to apply: **if it is not
an entity, it belongs in [../Expenses.Application](../Expenses.Application).**

An enum that describes an entity's own state is part of that entity and is **nested inside it** —
`Unit.UnitKind`, `Purchase.ExtractionState`, `FiscalInvoice.FiscalSource`,
`FiscalInvoice.FiscalCorroboration`.

## Signalling a broken rule

The domain has no exception type of its own, so entities throw the framework exceptions:

| Situation | Throw | Example |
| --- | --- | --- |
| Missing or contradictory argument | `ArgumentException` with `nameof(...)` | a blank description; a list price without a discount |
| A number outside its range or precision | `ArgumentOutOfRangeException(field, value, "...")` | a negative amount; five decimal places |
| An invariant of the whole entity | `InvalidOperationException` | expenses that do not reconcile; a parent cycle; a disallowed state transition |

Pass the offending value to `ArgumentOutOfRangeException` and put it in the message otherwise — that
is the only place the detail survives now. The stable error codes live in `ApplicationErrors`; the
use case that called the entity names the code (D22). Never reintroduce a code here.

## Conventions every entity follows

- `public sealed class`, private parameterless constructor commented `// EF materialisation.`
  assigning `null!` to non-nullable references.
- `public T Prop { get; private set; }` — all mutation goes through named methods.
- Construction through a **static factory** (`Record`, `Create`, `Propose`, `From`) that validates,
  trims strings, and returns a valid instance. No public constructor, no object-initialiser
  construction from outside.
- Validation is a `private static` method of the entity that owns the fields. `Expense` and
  `Purchase` each carry their own copy of the monetary rules (D7, D22); that repetition is
  deliberate, so do not "fix" it by extracting a shared type.
- Collections: `private readonly List<T> _items = [];` exposed as `IReadOnlyList<T>`.
- Rules are enforced *inside* the entity, never by a validator, an adapter or a database
  constraint (D2).
- Derived values (`DiscountPercentage`, `TotalSaving`, `SavingPercentage`, `Corroboration`) are
  computed properties and are **never stored** (D19). Nullable where "none printed" must stay
  distinguishable from zero.
- Verbatim receipt text (`CategoryRaw`, `UnitRaw`, `MerchantRaw`) is kept forever; matching later
  fills the id and must not erase the text (D9).
- `Amount` reconciles; `Quantity`, `UnitPrice`, `ListUnitPrice`, `DiscountAmount` are descriptive,
  may disagree, and are never used to recompute an amount (D6, D19).
- A discount is a **positive magnitude**, never a negative amount (D19).
