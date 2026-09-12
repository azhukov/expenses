using Expenses.Application.Errors;
using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;

namespace Expenses.Application.Services;

internal static class CategoryLookup
{
    public static async Task<Category> Require(
        this ICategoryRepository categories,
        string code,
        CancellationToken cancellationToken)
        => await categories.FindByCode(code, cancellationToken)
        ?? throw ExpensesException.For(
            ApplicationErrors.CategoryNotFound,
            $"There is no category with code '{code}'.",
            ("code", code));
}
