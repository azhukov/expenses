namespace Expenses.Desktop.Core.Ledger;

/// <summary>The arithmetic checks one extraction was put through.</summary>
public sealed record ArithmeticValidationReport(IReadOnlyList<ArithmeticCheck> Checks);
