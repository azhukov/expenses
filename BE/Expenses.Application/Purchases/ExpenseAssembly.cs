using Expenses.Application.Abstractions;
using Expenses.Application.Errors;
using Expenses.Domain;

namespace Expenses.Application.Purchases;

/// <summary>
/// Turns submitted lines into expenses: resolves reference data by code, refuses a retired entry,
/// and names the code for whatever rule the entity objects to. Shared by manual recording and by
/// confirming extraction candidates, because both produce the same shape of line (D2).
/// </summary>
internal static class ExpenseAssembly
{
    public static async Task<List<Expense>> Build(
        IReadOnlyList<ExpenseCommand> commands,
        ICategoryRepository categories,
        IUnitRepository units,
        CancellationToken cancellationToken)
    {
        var lines = new List<Expense>(commands.Count);

        foreach (var command in commands)
        {
            long? categoryId = await ResolveCategory(command.CategoryCode, categories, cancellationToken)
                ?? command.MatchedCategoryId;
            long? unitId = await ResolveUnit(command.UnitCode, units, cancellationToken)
                ?? command.MatchedUnitId;

            try
            {
                lines.Add(Expense.Record(
                    command.Description,
                    command.Amount,
                    command.Quantity,
                    unitId,
                    command.UnitPrice,
                    categoryId,
                    command.CategoryRaw,
                    command.UnitRaw,
                    command.ListUnitPrice,
                    command.DiscountAmount));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                throw DomainErrorTranslation.Expense(exception);
            }
        }

        return lines;
    }

    /// <summary>
    /// A retired entry stays readable on the expenses that already reference it, and is refused
    /// only for a new one — which is why this check lives here and not in the entity.
    /// </summary>
    private static async Task<long?> ResolveCategory(
        string? code,
        ICategoryRepository categories,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var category = await categories.FindByCode(code, cancellationToken)
            ?? throw ExpensesException.For(
                ApplicationErrors.CategoryNotFound,
                $"There is no category with code '{code}'.",
                ("categoryCode", code));

        return category.IsActive
            ? category.Id
            : throw ExpensesException.For(
                ApplicationErrors.CategoryInactive,
                $"The category '{code}' has been deactivated and cannot be assigned to a new expense.",
                ("categoryCode", code));
    }

    private static async Task<long?> ResolveUnit(
        string? code,
        IUnitRepository units,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var unit = await units.FindByCode(code, cancellationToken)
            ?? throw ExpensesException.For(
                ApplicationErrors.UnitNotFound,
                $"There is no unit with code '{code}'.",
                ("unitCode", code));

        return unit.IsActive
            ? unit.Id
            : throw ExpensesException.For(
                ApplicationErrors.UnitInactive,
                $"The unit '{code}' has been deactivated and cannot be assigned to a new expense.",
                ("unitCode", code));
    }
}
