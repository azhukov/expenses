namespace Expenses.Desktop.Core.Ledger;

/// <summary>One expense line as it is read back.</summary>
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
    decimal? DiscountPercentage);
