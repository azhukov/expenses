using Expenses.Application.Merchants;
using Expenses.Domain;

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

/// <summary>
/// What a caller resubmits from a capture response in order to confirm it. The server held none of
/// this: it is exactly what capture returned, echoed back verbatim or edited, since nothing about a
/// capture is retained server-side once the response is sent (D12).
/// </summary>
public sealed record CapturedReceiptCommand(
    Guid TempKey,
    Receipt.ExtractionState State,
    string? FailureReason = null,
    string? SuppliedIkof = null,
    string? SuppliedJikr = null,
    string? ExtractedIkof = null,
    string? ExtractedJikr = null,
    Receipt.FiscalSource FiscalExtractedSource = Receipt.FiscalSource.None,

    /// <summary>
    /// The invoice creation timestamp a fiscal QR decoded, if any — read verbatim from the capture
    /// response, not re-derived. Used only to default the purchase's occurrence when the caller
    /// supplies no date of its own.
    /// </summary>
    string? FiscalCreatedAt = null);

/// <summary>
/// <paramref name="OccurredAt"/> may be omitted only when confirming a capture whose fiscal QR
/// decoded an invoice creation timestamp: a manually recorded purchase always states its own date
/// (D5).
/// </summary>
public sealed record RecordPurchaseCommand(
    DateTime? OccurredAt,
    decimal Amount,
    IReadOnlyList<ExpenseCommand> Expenses,
    MerchantCommand? Merchant = null,
    CapturedReceiptCommand? Capture = null);

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
