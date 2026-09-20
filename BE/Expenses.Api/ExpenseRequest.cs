using Expenses.Application.Dtos;

namespace Expenses.Api;

/// <summary>
/// The request shapes the browser client posts. Binding is all these do: no rule may live in an
/// adapter (D1), so anything a request could be wrong about is decided by a use case.
/// </summary>
/// <param name="Description">The line's free-text description.</param>
/// <param name="Amount">The line's total amount.</param>
/// <param name="Quantity">The quantity purchased, if known.</param>
/// <param name="UnitCode">
/// The code of the unit the quantity is measured in. A line without one is refused — but the
/// parameter stays optional here so that the refusal is the ledger's own, naming the line, rather
/// than a binding failure that says only that the request could not be read (D1: no rule lives in
/// an adapter).
/// </param>
/// <param name="UnitPrice">The price per unit, if known.</param>
/// <param name="CategoryCode">The code of the assigned category, if known.</param>
/// <param name="CategoryRaw">The category as extracted, before mapping to a code.</param>
/// <param name="UnitRaw">The unit as extracted, before mapping to a code.</param>
/// <param name="ListUnitPrice">The undiscounted per-unit list price, if known.</param>
/// <param name="DiscountAmount">The discount applied to the line, if known.</param>
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
