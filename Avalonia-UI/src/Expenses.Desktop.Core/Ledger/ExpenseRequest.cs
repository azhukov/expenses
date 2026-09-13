namespace Expenses.Desktop.Core.Ledger;

/// <summary>
/// One line of a purchase as it is submitted. The raw texts travel beside the matched codes always,
/// because a match must never erase what the receipt printed.
/// </summary>
public sealed record ExpenseRequest(
    string Description,
    decimal Amount,
    decimal? Quantity,
    string? UnitCode,
    decimal? UnitPrice,
    string? CategoryCode,
    string? CategoryRaw,
    string? UnitRaw,
    decimal? ListUnitPrice,
    decimal? DiscountAmount);
