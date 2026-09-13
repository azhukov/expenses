namespace Expenses.Desktop.Core.Ledger;

/// <summary>A unit in the seeded dictionary, addressed by code.</summary>
public sealed record UnitView(
    long Id,
    string Code,
    string Name,
    string Symbol,
    UnitKind Kind,
    bool IsActive);
