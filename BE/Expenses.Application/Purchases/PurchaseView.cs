using Expenses.Domain;

namespace Expenses.Application.Purchases;

/// <summary>
/// One line as it is read back. <see cref="DiscountPercentage"/> is derived rather than stored, so
/// there is never a second source of truth for one fact (D19).
/// </summary>
public sealed record ExpenseView(
    long Id,
    string Description,
    decimal Quantity,
    decimal Amount,
    long? UnitId,
    decimal? UnitPrice,
    long? CategoryId,
    string? CategoryRaw,
    string? UnitRaw,
    decimal? ListUnitPrice,
    decimal? DiscountAmount,
    decimal? DiscountPercentage)
{
    public static ExpenseView Of(Expense expense) => new(
        expense.Id,
        expense.Description,
        expense.Quantity,
        expense.Amount,
        expense.UnitId,
        expense.UnitPrice,
        expense.CategoryId,
        expense.CategoryRaw,
        expense.UnitRaw,
        expense.ListUnitPrice,
        expense.DiscountAmount,
        expense.DiscountPercentage);
}

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
    bool HasReceipt,
    IReadOnlyList<ExpenseView> Expenses,
    decimal TotalSaving,
    decimal? SavingPercentage)
{
    /// <summary>
    /// Where the attached receipt has got to, so that reading a purchase answers "has the receipt
    /// been read yet" in the same read. Null when there is no receipt (D11).
    /// </summary>
    public Receipt.ExtractionState? ExtractionState { get; init; }

    public static PurchaseView Of(Purchase purchase) => new(
        purchase.Id,
        purchase.OccurredAt,
        purchase.Amount,
        purchase.MerchantId,
        purchase.MerchantRaw,
        purchase.Receipt is not null,
        purchase.Expenses.Select(ExpenseView.Of).ToList(),
        purchase.TotalSaving,
        purchase.SavingPercentage)
    {
        ExtractionState = purchase.Receipt?.State,
    };
}
