using System.Text.RegularExpressions;
using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Rules;
using Expenses.Desktop.Core.Tests.Fakes;

namespace Expenses.Desktop.Core.Tests.Rules;

/// <summary>
/// Ported case for case from FE/src/labels/labels.test.ts (D7). Scenarios from desktop-client: "A
/// known merchant is named", "An unmatched merchant falls back to what was printed", "Neither a
/// merchant nor verbatim text", "Identifiers are never displayed".
/// </summary>
public sealed class LabelsTests
{
    private static readonly MerchantView[] s_merchants = [new(7, "Mercadona", "A46103834", null, null, true)];

    private static readonly CategoryView[] s_categories = [new(3, "groceries", "Groceries", null, null, true, true)];

    [Fact]
    public void A_known_merchant_is_named()
    {
        Assert.Equal("Mercadona", Labels.Merchant(Purchase(7, "MERCADONA S.A."), s_merchants));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(99L)]
    public void An_unmatched_merchant_falls_back_to_what_was_printed(long? merchantId)
    {
        Assert.Equal("CAFE BAR PEPE", Labels.Merchant(Purchase(merchantId, "CAFE BAR PEPE"), s_merchants));
    }

    [Fact]
    public void The_fallback_holds_when_the_dictionary_could_not_be_read()
    {
        Assert.Equal("MERCADONA S.A.", Labels.Merchant(Purchase(7, "MERCADONA S.A."), []));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Neither_a_merchant_nor_verbatim_text(string? merchantRaw)
    {
        Assert.Equal(Labels.UnknownMerchant, Labels.Merchant(Purchase(null, merchantRaw), s_merchants));
    }

    [Fact]
    public void Identifiers_are_never_displayed_for_a_merchant()
    {
        string[] labels =
        [
            Labels.Merchant(Purchase(7, "MERCADONA S.A."), s_merchants),
            Labels.Merchant(Purchase(99, "CAFE BAR PEPE"), s_merchants),
            Labels.Merchant(Purchase(99, null), s_merchants),
        ];

        Assert.All(labels, label => Assert.DoesNotMatch(new Regex(@"\b(7|99)\b"), label));
    }

    [Fact]
    public void Identifiers_are_never_displayed_for_a_category()
    {
        Assert.Equal("Groceries", Labels.Category(3, s_categories, null));
        Assert.Equal(Labels.UnknownCategory, Labels.Category(41, s_categories, null));
        Assert.DoesNotContain("41", Labels.Category(41, s_categories, null), StringComparison.Ordinal);
        Assert.Equal(Labels.UnknownCategory, Labels.Category(null, s_categories, null));
    }

    [Fact]
    public void A_category_falls_back_the_same_way_a_merchant_does()
    {
        Assert.Equal("FRUTA", Labels.Category(41, s_categories, "FRUTA"));
        Assert.Equal("FRUTA", Labels.Category(null, s_categories, "FRUTA"));
    }

    private static PurchaseView Purchase(long? merchantId, string? merchantRaw)
    {
        return Purchases.Make(1, "2026-09-03T10:00:00", 12.5m, merchantId: merchantId, merchantRaw: merchantRaw);
    }
}
