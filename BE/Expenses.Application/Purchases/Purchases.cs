using Expenses.Application.Merchants;

namespace Expenses.Application.Purchases;

/// <summary>A line as submitted. Reference data is addressed by <c>code</c> (D8).</summary>
public sealed record ExpenseCommand(
    string Description,
    decimal Amount,
    decimal? Quantity = null,
    string? UnitCode = null,
    decimal? UnitPrice = null,
    string? CategoryCode = null,
    string? CategoryRaw = null,
    string? UnitRaw = null,
    decimal? ListUnitPrice = null,
    decimal? DiscountAmount = null)
{
    /// <summary>
    /// A reference already resolved to an identifier — how a confirmed extraction candidate
    /// carries the match a stage made, since a stage matches against the dictionary rather than
    /// producing a code. Ignored when the corresponding code is supplied.
    /// </summary>
    public long? MatchedCategoryId { get; init; }

    public long? MatchedUnitId { get; init; }
}

/// <summary>
/// Where the purchase was made, as printed. <paramref name="TaxId"/> is the authoritative match
/// where a receipt carried one (D18).
/// </summary>
public sealed record MerchantCommand(string Text, string? TaxId = null);

public sealed record RecordPurchaseCommand(
    DateTime OccurredAt,
    decimal Amount,
    IReadOnlyList<ExpenseCommand> Expenses,
    MerchantCommand? Merchant = null);

/// <summary>
/// Distinguishes a newly created purchase from one that was already recorded (D3). Both are
/// successes; an assistant that receives an error routes around it, one that receives
/// "already recorded, here it is" simply proceeds.
/// </summary>
public sealed record RecordPurchaseResult(
    PurchaseView Purchase,
    bool AlreadyRecorded,
    MerchantView? Merchant = null,
    bool MerchantNewlyAdded = false);

public sealed record ListPurchasesQuery(DateOnly? From = null, DateOnly? To = null, int Skip = 0, int? Take = null);
