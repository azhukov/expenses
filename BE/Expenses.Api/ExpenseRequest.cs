using Expenses.Application.Dtos;

namespace Expenses.Api;

/// <summary>
/// The request shapes the browser client posts. Binding is all these do: no rule may live in an
/// adapter (D1), so anything a request could be wrong about is decided by a use case.
/// </summary>
/// <param name="DiscountPercentage">
/// Accepted only so that it can be refused: a percentage is a rounded rendering of two numbers
/// already stored, and keeping it would be a second, lossy source of truth (D19).
/// </param>
public sealed record ExpenseRequest(
    string Description,
    decimal Amount,
    decimal? Quantity = null,
    string? UnitCode = null,
    decimal? UnitPrice = null,
    string? CategoryCode = null,
    string? CategoryRaw = null,
    string? UnitRaw = null,
    decimal? ListUnitPrice = null,
    decimal? DiscountAmount = null,

    decimal? DiscountPercentage = null)
{
    public ExpenseCommand ToCommand() => new(
        Description,
        Amount,
        Quantity,
        UnitCode,
        UnitPrice,
        CategoryCode,
        CategoryRaw,
        UnitRaw,
        ListUnitPrice,
        DiscountAmount);
}
