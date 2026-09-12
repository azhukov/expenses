namespace Expenses.Domain.Entities;

/// <summary>
/// A line within a <see cref="Purchase"/>. Has no repository and no independent lifecycle (D2).
///
/// <see cref="Amount"/> is authoritative. <see cref="Quantity"/>, <see cref="UnitPrice"/>,
/// <see cref="ListUnitPrice"/> and <see cref="DiscountAmount"/> are descriptive: they are
/// permitted to disagree with the amount and are never used to recompute it (D6, D19).
/// </summary>
public sealed class Expense
{
    /// <summary>Fractional mass and volume; three decimals covers fuel volumes (D10).</summary>
    private const int QuantityMaxScale = 3;

    /// <summary>Money is exact to two decimal places and never negative (D7).</summary>
    private const int AmountMaxScale = 2;

    /// <summary>The value a quantity takes when none was supplied.</summary>
    private const decimal DefaultQuantity = 1m;

    private Expense()
    {
        // EF materialisation.
        Description = null!;
    }

    public long Id { get; private set; }

    public string Description { get; private set; }

    /// <summary>
    /// How much of the thing was bought. Non-nullable and defaulting to 1, so the simple case
    /// reads coherently without a nullable every consumer must guard (D6). Descriptive only.
    /// </summary>
    public decimal Quantity { get; private set; }

    public decimal Amount { get; private set; }

    public long? UnitId { get; private set; }

    public decimal? UnitPrice { get; private set; }

    public long? CategoryId { get; private set; }

    /// <summary>Category text exactly as printed, retained whether or not it matched (D9).</summary>
    public string? CategoryRaw { get; private set; }

    /// <summary>Unit text exactly as printed, retained whether or not it matched (D9).</summary>
    public string? UnitRaw { get; private set; }

    /// <summary>What the item normally costs. Null means the source printed no discount (D19).</summary>
    public decimal? ListUnitPrice { get; private set; }

    /// <summary>A positive magnitude taken off the list price. Null means no discount was printed (D19).</summary>
    public decimal? DiscountAmount { get; private set; }

    /// <summary>
    /// Derived for display only; never stored (D19). Null when no discount was printed, so that
    /// "not discounted" stays distinguishable from "discounted by nothing".
    /// </summary>
    public decimal? DiscountPercentage
    {
        get
        {
            if (ListUnitPrice is not { } list || DiscountAmount is not { } discount || list == 0m)
            {
                return null;
            }

            return Math.Round(discount / list * 100m, 2);
        }
    }

    public static Expense Record(
        string description,
        decimal amount,
        decimal? quantity = null,
        long? unitId = null,
        decimal? unitPrice = null,
        long? categoryId = null,
        string? categoryRaw = null,
        string? unitRaw = null,
        decimal? listUnitPrice = null,
        decimal? discountAmount = null)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("An expense requires a description.", nameof(description));
        }

        // Set together or not at all — the pairing is what makes null mean "no discount printed" (D19).
        if (listUnitPrice.HasValue != discountAmount.HasValue)
        {
            throw new ArgumentException(
                "A list unit price and a discount amount are recorded together or not at all.",
                listUnitPrice.HasValue ? nameof(discountAmount) : nameof(listUnitPrice));
        }

        // Checked before the general amount rule, so the error names the discount rather than
        // reporting a bare non-negativity violation the caller cannot place.
        if (discountAmount is < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(discountAmount),
                discountAmount,
                "A discount is a positive magnitude.");
        }

        if (quantity is < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "A quantity cannot be negative.");
        }

        return new Expense
        {
            Description = description.Trim(),
            Amount = ValidateAmount(amount, nameof(amount)),
            Quantity = quantity is { } suppliedQuantity ? NormaliseQuantity(suppliedQuantity) : DefaultQuantity,
            UnitId = unitId,
            UnitPrice = ValidateAmount(unitPrice, nameof(unitPrice)),
            CategoryId = categoryId,
            CategoryRaw = categoryRaw,
            UnitRaw = unitRaw,
            ListUnitPrice = ValidateAmount(listUnitPrice, nameof(listUnitPrice)),
            DiscountAmount = ValidateAmount(discountAmount, nameof(discountAmount)),
        };
    }

    /// <summary>Matching a unit later must not erase the verbatim text (D9).</summary>
    public void MatchUnit(long unitId) => UnitId = unitId;

    /// <summary>Matching a category later must not erase the verbatim text (D9).</summary>
    public void MatchCategory(long categoryId) => CategoryId = categoryId;

    /// <summary>
    /// The two rules every monetary amount obeys (D7): non-negative, and at most two decimal
    /// places. Deliberately a private function of the entity that owns the fields rather than a
    /// shared type over a <see cref="decimal"/>; <see cref="Purchase"/> validates its own amount
    /// the same way, and the small repetition is the price of keeping the domain to entities.
    ///
    /// Returns the amount at the precision it was supplied with. Trailing zeros beyond four places
    /// are dropped; a significant digit beyond them is an error rather than something to round
    /// away silently.
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

    /// <summary>Validates an optional amount, leaving absence alone.</summary>
    private static decimal? ValidateAmount(decimal? value, string field)
        => value is { } supplied ? ValidateAmount(supplied, field) : null;

    /// <summary>
    /// Trailing zeros beyond three places are dropped; a significant digit beyond them is an
    /// error rather than something to round away silently.
    /// </summary>
    private static decimal NormaliseQuantity(decimal quantity)
    {
        if (quantity.Scale <= QuantityMaxScale)
        {
            return quantity;
        }

        decimal rounded = Math.Round(quantity, QuantityMaxScale);
        if (rounded != quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                $"A quantity carries at most {QuantityMaxScale} decimal places.");
        }

        return rounded;
    }
}
