namespace Expenses.Desktop.Core.Ledger;

/// <summary>A category in the seeded and user-extended dictionary, addressed by code.</summary>
public sealed record CategoryView(
    long Id,
    string Code,
    string Name,
    long? ParentId,
    string? ParentCode,
    bool IsSystem,
    bool IsActive);
