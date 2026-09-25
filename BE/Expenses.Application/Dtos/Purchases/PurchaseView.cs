using Expenses.Domain.Entities;

namespace Expenses.Application.Dtos;

/// <summary>
/// A purchase as it is read back. <see cref="MerchantRaw"/> travels beside
/// <see cref="MerchantId"/> always, because a later match must never erase what was printed (D9).
/// </summary>
public sealed record PurchaseView(
    long Id,
    DateTime OccurredAt,
    decimal Amount,
    long? MerchantId,
    string? MerchantRaw,
    bool HasReceiptImage,
    IReadOnlyList<ExpenseView> Expenses,
    decimal TotalSaving,
    decimal? SavingPercentage)
{
    /// <summary>
    /// The fiscal invoice the purchase was recorded against, reported apart from its image: a purchase
    /// read from its fiscal code alone has this and no image (D41).
    /// </summary>
    public FiscalInvoiceView? Fiscal { get; init; }

    /// <summary>
    /// Where the attached receipt has got to, so that reading a purchase answers "has the receipt
    /// been read yet" in the same read. Null when there is neither an image nor a fiscal invoice (D35).
    /// </summary>
    public Purchase.ExtractionState? ExtractionState { get; init; }

    public static PurchaseView Of(Purchase purchase) => new(
        purchase.Id,
        purchase.OccurredAt,
        purchase.Amount,
        purchase.MerchantId,
        purchase.MerchantRaw,
        purchase.Receipt is not null,
        [.. purchase.Expenses.Select(ExpenseView.Of)],
        purchase.TotalSaving,
        purchase.SavingPercentage)
    {
        ExtractionState = purchase.Extraction,
        Fiscal = purchase.Fiscal is { } fiscal ? FiscalInvoiceView.Of(fiscal) : null,
    };
}
