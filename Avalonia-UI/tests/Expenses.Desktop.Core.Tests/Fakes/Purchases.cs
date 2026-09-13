using Expenses.Desktop.Core.Ledger;

namespace Expenses.Desktop.Core.Tests.Fakes;

/// <summary>Purchases as the ledger would return them, with only what a test cares about spelled out.</summary>
public static class Purchases
{
    public static PurchaseView Make(
        long id,
        string occurredAt,
        decimal amount,
        ExtractionState? state = null,
        long? merchantId = null,
        string? merchantRaw = null,
        int lines = 0)
    {
        var expenses = Enumerable.Range(1, lines)
            .Select(line => new ExpenseView(id * 100 + line, $"Line {line}", 1m, 1m, null, null, null, null, null, null, null, null))
            .ToList();

        return new PurchaseView(
            id,
            DateTime.Parse(occurredAt, System.Globalization.CultureInfo.InvariantCulture),
            amount,
            merchantId,
            merchantRaw,
            state is not null,
            expenses,
            0m,
            null,
            state);
    }
}
