namespace Expenses.Domain;

/// <summary>
/// The only aggregate root (D2). A single act of paying, containing one or more expenses.
/// A manually typed entry is the degenerate case: one purchase, one expense, no image.
/// </summary>
public sealed class Purchase
{
    /// <summary>Money is exact to two decimal places and never negative (D7).</summary>
    private const int AmountMaxScale = 2;

    private readonly List<Expense> _expenses = [];

    private Purchase()
    {
        // EF materialisation.
    }

    public long Id { get; private set; }

    /// <summary>Wall-clock, <see cref="DateTimeKind.Unspecified"/>, never converted (D5).</summary>
    public DateTime OccurredAt { get; private set; }

    public decimal Amount { get; private set; }

    public long? MerchantId { get; private set; }

    /// <summary>Merchant text exactly as printed, retained whether or not it matched (D9, D18).</summary>
    public string? MerchantRaw { get; private set; }

    /// <summary>
    /// Zero or one receipt, carried by the purchase itself (D11). Null for a manual entry. Two
    /// purchases with byte-identical receipts share one file in the store, never this value.
    /// </summary>
    public Receipt? Receipt { get; private set; }

    public IReadOnlyList<Expense> Expenses => _expenses;

    /// <summary>Sum of the line discounts. Derived, never stored (D19).</summary>
    public decimal TotalSaving => _expenses.Sum(expense => expense.DiscountAmount ?? 0m);

    /// <summary>
    /// Derived for display only. Null when nothing on the purchase carried a discount, so that a
    /// purchase with no discounts is not reported as having saved zero percent.
    /// </summary>
    public decimal? SavingPercentage
    {
        get
        {
            if (!_expenses.Any(expense => expense.DiscountAmount.HasValue))
            {
                return null;
            }

            var listTotal = Amount + TotalSaving;
            return listTotal == 0m ? null : Math.Round(TotalSaving / listTotal * 100m, 2);
        }
    }

    public static Purchase Record(
        DateTime occurredAt,
        decimal amount,
        IEnumerable<Expense> expenses,
        long? merchantId = null,
        string? merchantRaw = null) =>
        Create(NormaliseOccurrence(occurredAt), amount, expenses, merchantId, merchantRaw);

    public static Purchase Record(
        DateOnly occurredOn,
        decimal amount,
        IEnumerable<Expense> expenses,
        long? merchantId = null,
        string? merchantRaw = null) =>
        Create(occurredOn.ToDateTime(TimeOnly.MinValue), amount, expenses, merchantId, merchantRaw);

    /// <summary>Matching a merchant later must not erase the verbatim text (D9, D18).</summary>
    public void MatchMerchant(long merchantId) => MerchantId = merchantId;

    /// <summary>
    /// Attaches the one receipt this purchase may have. Rejects a second and leaves the existing
    /// one untouched.
    /// </summary>
    public void AttachReceipt(Receipt receipt)
    {
        if (Receipt is not null)
        {
            throw new InvalidOperationException("This purchase already has a receipt.");
        }

        Receipt = receipt;
    }

    /// <summary>
    /// Removes the receipt from the purchase and reports the file it referred to, so the caller
    /// can delete those bytes once nothing else references them (D11). The expenses already
    /// confirmed from it are not touched: they are the ledger, and the image was only the source.
    /// </summary>
    public Receipt? DetachReceipt()
    {
        var detached = Receipt;
        Receipt = null;

        return detached;
    }

    /// <summary>
    /// Replaces the expenses of the purchase, re-applying the reconciliation invariant.
    /// Used when extraction candidates are confirmed (D16). The existing expenses are left
    /// untouched if the replacement does not reconcile.
    /// </summary>
    public void ReplaceExpenses(IEnumerable<Expense> expenses)
    {
        var replacement = Validated(Amount, expenses);

        _expenses.Clear();
        _expenses.AddRange(replacement);
    }

    private static Purchase Create(
        DateTime occurredAt,
        decimal amount,
        IEnumerable<Expense> expenses,
        long? merchantId,
        string? merchantRaw)
    {
        var purchaseAmount = ValidateAmount(amount, nameof(amount));
        var lines = Validated(purchaseAmount, expenses);

        var purchase = new Purchase
        {
            OccurredAt = occurredAt,
            Amount = purchaseAmount,
            MerchantId = merchantId,
            MerchantRaw = merchantRaw,
        };
        purchase._expenses.AddRange(lines);

        return purchase;
    }

    /// <summary>
    /// The defining rule of the type, enforced inside the aggregate rather than by a validator or a
    /// database constraint (D2). Only <see cref="Expense.Amount"/> participates — discounts and
    /// unit prices never do (D6, D19).
    /// </summary>
    private static List<Expense> Validated(decimal amount, IEnumerable<Expense> expenses)
    {
        var lines = expenses.ToList();

        if (lines.Count == 0)
        {
            throw new InvalidOperationException("A purchase requires at least one expense.");
        }

        var sum = lines.Sum(expense => expense.Amount);
        if (sum != amount)
        {
            throw new InvalidOperationException(
                $"The purchase amount is {amount} while its expenses sum to {sum}.");
        }

        return lines;
    }

    /// <summary>
    /// The two rules every monetary amount obeys (D7), applied to the purchase amount.
    /// <see cref="Expense"/> validates its own amounts the same way; see the note there on why
    /// this is repeated rather than shared.
    /// </summary>
    private static decimal ValidateAmount(decimal value, string field)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(field, value, "A monetary amount cannot be negative.");
        }

        if (value.Scale <= AmountMaxScale)
        {
            return value;
        }

        var rounded = Math.Round(value, AmountMaxScale);
        if (rounded != value)
        {
            throw new ArgumentOutOfRangeException(
                field,
                value,
                $"A monetary amount carries at most {AmountMaxScale} decimal places.");
        }

        return rounded;
    }

    /// <summary>
    /// No timezone conversion is ever applied (D5): the value is what the receipt printed, and its
    /// kind is always <see cref="DateTimeKind.Unspecified"/>. A value arriving as UTC or Local
    /// keeps the same year, month, day, hour, minute and second. A date with no time of day
    /// becomes 00:00:00 on that date, which is what the <see cref="DateOnly"/> overload relies on.
    /// </summary>
    private static DateTime NormaliseOccurrence(DateTime occurredAt) =>
        DateTime.SpecifyKind(occurredAt, DateTimeKind.Unspecified);
}
