using Expenses.Domain.Entities;

namespace Expenses.Application.Dtos;

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
