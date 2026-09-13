namespace Expenses.Desktop.Core.Ledger;

/// <summary>A merchant in the dictionary the ledger learns.</summary>
public sealed record MerchantView(
    long Id,
    string Name,
    string? TaxId,
    long? ParentId,
    string? ParentName,
    bool IsActive);
