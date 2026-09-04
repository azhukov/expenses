using Expenses.Application.Errors;

namespace Expenses.Api;

/// <summary>
/// The one thing the adapter refuses on its own, shared by the two actions that accept expense
/// lines. It refuses rather than interprets (D19).
/// </summary>
internal static class RequestGuards
{
    /// <summary>
    /// A discount percentage is not a value this interface accepts in place of an amount: the
    /// percentage is a rounded rendering of two numbers already stored (D19).
    /// </summary>
    public static void RejectDiscountPercentage(IEnumerable<ExpenseRequest>? expenses)
    {
        if (expenses?.Any(expense => expense.DiscountPercentage.HasValue) != true)
        {
            return;
        }

        throw ExpensesException.For(
            ApplicationErrors.ExpenseDiscountPercentageNotAccepted,
            "A discount is recorded as an amount; the percentage is derived for display.",
            ("discountPercentage", null));
    }
}
