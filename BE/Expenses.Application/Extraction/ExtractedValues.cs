namespace Expenses.Application.Extraction;

/// <summary>
/// Names of the values a stage can produce, used as provenance and confidence keys.
/// Stable, because both adapters report them.
/// </summary>
public static class ExtractedValues
{
    public const string Description = "description";
    public const string Amount = "amount";
    public const string Quantity = "quantity";
    public const string UnitPrice = "unit_price";
    public const string ListUnitPrice = "list_unit_price";
    public const string DiscountAmount = "discount_amount";
    public const string CategoryGuess = "category_guess";
    public const string UnitGuess = "unit_guess";
    public const string MerchantName = "merchant_name";
    public const string Total = "total";
    public const string TaxRatePercent = "tax_rate_percent";
    public const string TaxAmount = "tax_amount";
}
