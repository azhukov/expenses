using Expenses.Application.Interfaces;

namespace Expenses.Application.Dtos;

/// <summary>
/// A search hit. <see cref="Merchant"/> is null where the term matched only the verbatim text on a
/// purchase вЂ” the unmatched merchant is exactly the one a user searches for (D14).
/// </summary>
public sealed record MerchantMatchView(MerchantView? Merchant, string MatchedText, long? PurchaseId)
{
    public static MerchantMatchView Of(MerchantMatch match) => new(
        match.Merchant is null ? null : MerchantView.Of(match.Merchant),
        match.MatchedText,
        match.PurchaseId);
}
