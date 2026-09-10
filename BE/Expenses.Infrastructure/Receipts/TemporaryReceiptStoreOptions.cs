namespace Expenses.Infrastructure.Receipts;

internal sealed class TemporaryReceiptStoreOptions
{
    /// <summary>
    /// Where captures wait to be confirmed. A distinct root from the permanent store's, so a naive
    /// directory walk over either one never mistakes an unconfirmed capture for a confirmed receipt.
    /// </summary>
    public string RootPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "receipts-temp");
}
