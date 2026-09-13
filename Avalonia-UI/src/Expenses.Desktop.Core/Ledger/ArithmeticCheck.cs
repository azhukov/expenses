namespace Expenses.Desktop.Core.Ledger;

/// <summary>One arithmetic check, carrying the values that disagreed.</summary>
public sealed record ArithmeticCheck(
    string Name,
    CheckOutcome Outcome,
    string Description,
    IReadOnlyDictionary<string, decimal?> Values);
