using Expenses.Application.Abstractions;
using Expenses.Domain;

namespace Expenses.Application.Merchants;

/// <summary>
/// A merchant as it is read back. Unlike a category it has no code: its identity is
/// <see cref="TaxId"/> where the receipt printed one, and nothing where it did not (D18).
/// </summary>
public sealed record MerchantView(
    long Id,
    string Name,
    string? TaxId,
    long? ParentId,
    string? ParentName,
    bool IsActive)
{
    public static MerchantView Of(Merchant merchant) => new(
        merchant.Id,
        merchant.Name,
        merchant.TaxId,
        merchant.Parent?.Id ?? merchant.ParentId,
        merchant.Parent?.Name,
        merchant.IsActive);
}

/// <summary>
/// A search hit. <see cref="Merchant"/> is null where the term matched only the verbatim text on a
/// purchase — the unmatched merchant is exactly the one a user searches for (D14).
/// </summary>
public sealed record MerchantMatchView(MerchantView? Merchant, string MatchedText, long? PurchaseId)
{
    public static MerchantMatchView Of(MerchantMatch match) => new(
        match.Merchant is null ? null : MerchantView.Of(match.Merchant),
        match.MatchedText,
        match.PurchaseId);
}
