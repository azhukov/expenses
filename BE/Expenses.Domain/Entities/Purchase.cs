namespace Expenses.Domain.Entities;

/// <summary>
/// The only aggregate root (D2). A single act of paying, containing one or more expenses.
/// A manually typed entry is the degenerate case: one purchase, one expense, no image.
/// </summary>
public sealed class Purchase
{
    /// <summary>Money is exact to two decimal places and never negative (D7).</summary>
    private const int AmountMaxScale = 2;

    /// <summary>
    /// Where extraction of the purchase's receipt got to. Every member is terminal: extraction always
    /// runs to completion within the request that triggered it, so there is nothing between requests
    /// for a receipt to sit in. The numeric values are unchanged from when <c>Pending</c> and
    /// <c>Extracting</c> existed, so persisted terminal states need no remapping.
    /// </summary>
    public enum ExtractionState
    {
        Extracted = 2,
        NeedsReview = 3,
        Failed = 4,
    }

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
    /// Zero or one receipt image, carried by the purchase itself (D11). Null for a manual entry and
    /// for a purchase read from its fiscal code alone. Two purchases with byte-identical receipts
    /// share one file in the store, never this value.
    /// </summary>
    public Receipt? Receipt { get; private set; }

    /// <summary>
    /// The tax-authority invoice the purchase was recorded against, independent of the image (D35).
    /// Null where no fiscal code was read, supplied or answered for; never an empty invoice.
    /// </summary>
    public FiscalInvoice? Fiscal { get; private set; }

    /// <summary>
    /// Where extraction of the receipt got to. Present exactly when the purchase carries an image or
    /// a fiscal invoice, since those are what extraction reads (D35).
    /// </summary>
    public ExtractionState? Extraction { get; private set; }

    /// <summary>Why extraction failed, when it did. Null otherwise.</summary>
    public string? ExtractionFailureReason { get; private set; }

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

            decimal listTotal = Amount + TotalSaving;
            return listTotal == 0m ? null : Math.Round(TotalSaving / listTotal * 100m, 2);
        }
    }

    /// <summary>
    /// A purchase acquires its receipt, if any, at the moment it is created and never afterward:
    /// confirming a capture is the only way a purchase ever gets one, and that happens here, in the
    /// same call that establishes its amount and lines (D11). What the capture read — an image, a
    /// fiscal invoice, or both — arrives with the terminal state extraction reached, and the state
    /// arrives exactly when one of them does (D35).
    /// </summary>
    public static Purchase Record(
        DateTime occurredAt,
        decimal amount,
        IEnumerable<Expense> expenses,
        long? merchantId = null,
        string? merchantRaw = null,
        Receipt? receipt = null,
        FiscalInvoice? fiscal = null,
        ExtractionState? extraction = null,
        string? extractionFailureReason = null)
        => Create(
            NormaliseOccurrence(occurredAt),
            amount,
            expenses,
            merchantId,
            merchantRaw,
            receipt,
            fiscal,
            extraction,
            extractionFailureReason);

    public static Purchase Record(
        DateOnly occurredOn,
        decimal amount,
        IEnumerable<Expense> expenses,
        long? merchantId = null,
        string? merchantRaw = null,
        Receipt? receipt = null,
        FiscalInvoice? fiscal = null,
        ExtractionState? extraction = null,
        string? extractionFailureReason = null)
        => Create(
            occurredOn.ToDateTime(TimeOnly.MinValue),
            amount,
            expenses,
            merchantId,
            merchantRaw,
            receipt,
            fiscal,
            extraction,
            extractionFailureReason);

    /// <summary>Matching a merchant later must not erase the verbatim text (D9, D18).</summary>
    public void MatchMerchant(long merchantId) => MerchantId = merchantId;

    /// <summary>
    /// Removes the receipt image from the purchase and reports the file it referred to, so the
    /// caller can delete those bytes once nothing else references them (D11). The expenses already
    /// confirmed from it are not touched: they are the ledger, and the image was only the source. A
    /// fiscal invoice stays, and with it the extraction state; with neither left, the state goes too,
    /// because it would describe nothing (D35).
    /// </summary>
    public Receipt? DetachReceipt()
    {
        var detached = Receipt;
        Receipt = null;

        if (Fiscal is null)
        {
            Extraction = null;
            ExtractionFailureReason = null;
        }

        return detached;
    }

    /// <summary>
    /// Gives a purchase read from an image the fiscal invoice a later run decoded from that image.
    /// It is not a way to acquire a receipt after creation: only a purchase that already has an
    /// extraction state may learn this, and only once, because a payload already held is preferred
    /// outright over a later reading of the same image (D31). An empty invoice is ignored.
    /// </summary>
    public void AttachFiscalInvoice(FiscalInvoice fiscal)
    {
        if (fiscal.IsEmpty)
        {
            return;
        }

        if (Extraction is null)
        {
            throw new InvalidOperationException(
                "Only a purchase that was read from a receipt can gain the fiscal invoice it carried.");
        }

        if (Fiscal is not null)
        {
            throw new InvalidOperationException("The purchase already carries a fiscal invoice.");
        }

        Fiscal = fiscal;
    }

    /// <summary>
    /// Re-extracting the receipt: every state is terminal, so this is always a single, direct
    /// terminal-to-terminal move, never a multi-step lifecycle.
    /// </summary>
    public void TransitionExtraction(ExtractionState next, string? failureReason = null)
    {
        if (Extraction is null)
        {
            throw new InvalidOperationException("The purchase carries no receipt to extract.");
        }

        Extraction = next;
        ExtractionFailureReason = next == ExtractionState.Failed ? failureReason : null;
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
        string? merchantRaw,
        Receipt? receipt,
        FiscalInvoice? fiscal,
        ExtractionState? extraction,
        string? extractionFailureReason)
    {
        decimal purchaseAmount = ValidateAmount(amount, nameof(amount));
        var lines = Validated(purchaseAmount, expenses);

        var carried = fiscal is { IsEmpty: false } ? fiscal : null;

        // The state describes what was read, so it exists exactly when something was (D35).
        if ((receipt is not null || carried is not null) != extraction.HasValue)
        {
            throw new InvalidOperationException(extraction.HasValue
                ? "An extraction state requires a receipt image or a fiscal invoice to describe."
                : "A receipt image or fiscal invoice requires the extraction state it reached.");
        }

        var purchase = new Purchase
        {
            OccurredAt = occurredAt,
            Amount = purchaseAmount,
            MerchantId = merchantId,
            MerchantRaw = merchantRaw,
            Receipt = receipt,
            Fiscal = carried,
            Extraction = extraction,
            ExtractionFailureReason = extraction == ExtractionState.Failed ? extractionFailureReason : null,
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

        decimal sum = lines.Sum(expense => expense.Amount);
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

        decimal rounded = Math.Round(value, AmountMaxScale);
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
    private static DateTime NormaliseOccurrence(DateTime occurredAt)
        => DateTime.SpecifyKind(occurredAt, DateTimeKind.Unspecified);
}
