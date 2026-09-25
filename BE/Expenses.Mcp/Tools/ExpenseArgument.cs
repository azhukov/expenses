using System.ComponentModel;
using Expenses.Application.Dtos;

namespace Expenses.Mcp.Tools;

/// <summary>
/// A line as an assistant supplies it. Reference data is named by <c>code</c>, never by display
/// name: <c>GROCERIES</c> is unambiguous where "Groceries" is not (D8).
/// </summary>
public sealed record ExpenseArgument(
    [property: Description("What was bought, as it should read in the ledger.")]
    string Description,
    [property: Description("What was actually paid for this line. Authoritative.")]
    decimal Amount,
    [property: Description("How much was bought. Defaults to 1.")]
    decimal? Quantity = null,
    // Optional in the shape, required by the ledger: a missing unit must come back as the
    // ledger's own error naming the line, not as an argument the host could not bind.
    [property: Description("Unit code, such as KG. Required, and never a display name.")]
    string? UnitCode = null,
    [property: Description("Price per unit. Descriptive: it need not multiply out to the amount.")]
    decimal? UnitPrice = null,
    [property: Description("Category code, such as GROCERIES. Never a display name.")]
    string? CategoryCode = null,
    [property: Description("What the item normally costs, when a discount was printed.")]
    decimal? ListUnitPrice = null,
    [property: Description("How much was taken off, as a positive amount. Never a percentage.")]
    decimal? DiscountAmount = null)
{
    public ExpenseCommand ToCommand() => new(
        Description,
        Amount,
        Quantity,
        UnitCode,
        UnitPrice,
        CategoryCode,
        ListUnitPrice: ListUnitPrice,
        DiscountAmount: DiscountAmount);
}
