using Expenses.Desktop.Core.Ledger;

namespace Expenses.Desktop.Core.Rules;

/// <summary>Display names resolved from reference data (ported from FE/src/labels, D7).</summary>
public static class Labels
{
    public const string UnknownMerchant = "Unknown merchant";

    public const string UnknownCategory = "Uncategorised";

    /// <summary>
    /// The fixed fallback chain: the dictionary's name, else the verbatim receipt text, else "Unknown
    /// merchant". The middle link is the honest display for a merchant not yet learned, not a
    /// workaround. An identifier is never shown, and a purchase is never withheld for want of a name.
    /// </summary>
    public static string Merchant(PurchaseView purchase, IReadOnlyList<MerchantView> merchants)
    {
        var matched = purchase.MerchantId is { } id ? merchants.FirstOrDefault(merchant => merchant.Id == id) : null;

        return Present(matched?.Name) ?? Present(purchase.MerchantRaw) ?? UnknownMerchant;
    }

    /// <summary>The same discipline for a category: the dictionary, then what the receipt printed, then neither.</summary>
    public static string Category(long? categoryId, IReadOnlyList<CategoryView> categories, string? categoryRaw)
    {
        var matched = categoryId is { } id ? categories.FirstOrDefault(category => category.Id == id) : null;

        return Present(matched?.Name) ?? Present(categoryRaw) ?? UnknownCategory;
    }

    private static string? Present(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
