using System.Globalization;
using Expenses.Application.Dtos;
using Expenses.Application.Errors;
using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;

namespace Expenses.Application.Services;

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
        IUnitAliasRepository aliases,
        CancellationToken cancellationToken,
        List<string>? warnings = null)
    {
        var lines = new List<Expense>(commands.Count);

        // Loaded only when some line prints a unit, which is the only time it is consulted.
        var lookup = commands.Any(command => UnitText.Normalise(command.UnitRaw) is not null)
            ? await UnitLookup.Load(units, aliases, cancellationToken)
            : null;

        foreach (var (command, position) in commands.Select((command, index) => (command, index + 1)))
        {
            long? categoryId = await ResolveCategory(command.CategoryCode, categories, cancellationToken)
                ?? command.MatchedCategoryId;
            long? unitId = await ResolveUnit(command.UnitCode, units, cancellationToken)
                ?? command.MatchedUnitId
                ?? lookup?.Match(command.UnitRaw);

            // Checked here rather than in the entity: a unit reaches a line as a code, as a match
            // made while extracting, or as the printed text matched now, and this is the one place
            // all three have been consulted. The entity cannot state the rule either way, since
            // expenses recorded before it have no unit and must still load.
            if (unitId is null)
            {
                throw ExpensesException.For(
                    ApplicationErrors.ExpenseUnitRequired,
                    $"Expense line {position} requires a unit.",
                    ("line", position));
            }

            // A person said what this printed text means, and nothing yet did: that is an alias.
            // Known text is left alone even when this line chose differently, so one line never
            // re-points a rule every later receipt relies on. Tracked rather than saved, so it
            // commits with the expense or not at all (D16).
            if (lookup is not null
                && UnitText.Normalise(command.UnitRaw) is { } printed
                && !lookup.IsKnown(printed))
            {
                await aliases.Add(UnitAlias.Create(unitId.Value, printed), cancellationToken);
                lookup.Remember(printed, unitId.Value);
            }

            lines.Add(Record(command, unitId, categoryId, position, warnings));
        }

        return lines;
    }

    /// <summary>
    /// The values a person confirming a capture never sees: the review screen edits the description,
    /// amount, quantity, unit and category, and carries these through as extraction read them.
    /// </summary>
    private static readonly HashSet<string> s_unseen = ["unitPrice", "listUnitPrice", "discountAmount"];

    /// <summary>
    /// Records one line. Where <paramref name="warnings"/> is given — a capture being confirmed — a
    /// rule the entity enforces on a value the confirming person could not see does not refuse the
    /// purchase: nothing they could do on the screen would satisfy it. The value is dropped instead,
    /// both halves of a discount together (D19), and the reason is reported as a warning. The rule
    /// itself is still the entity's; this only decides what a breach of it costs.
    /// </summary>
    private static Expense Record(
        ExpenseCommand command,
        long? unitId,
        long? categoryId,
        int position,
        List<string>? warnings)
    {
        while (true)
        {
            try
            {
                return Expense.Record(
                    command.Description,
                    command.Amount,
                    command.Quantity,
                    unitId,
                    command.UnitPrice,
                    categoryId,
                    command.CategoryRaw,
                    command.UnitRaw,
                    command.ListUnitPrice,
                    command.DiscountAmount);
            }
            catch (ArgumentException exception)
                when (warnings is not null && exception.ParamName is { } field && s_unseen.Contains(field))
            {
                if (field == "unitPrice")
                {
                    warnings.Add($"Line {position}: the unit price {command.UnitPrice} was not kept. {Reason(exception)}");
                    command = command with { UnitPrice = null };
                }
                else
                {
                    warnings.Add(
                        $"Line {position}: the discount (list price {Shown(command.ListUnitPrice)}, "
                        + $"discount {Shown(command.DiscountAmount)}) was not kept. {Reason(exception)}");
                    command = command with { ListUnitPrice = null, DiscountAmount = null };
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                throw DomainErrorTranslation.Expense(exception);
            }
        }
    }

    private static string Shown(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "none";

    /// <summary>The entity's own sentence, without the parameter name .NET appends to it.</summary>
    private static string Reason(ArgumentException exception)
    {
        string message = exception.Message;
        int suffix = message.IndexOf(" (Parameter '", StringComparison.Ordinal);

        if (suffix >= 0)
        {
            message = message[..suffix];
        }

        int actual = message.IndexOf("\nActual value", StringComparison.Ordinal);

        return (actual >= 0 ? message[..actual] : message).Trim();
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
