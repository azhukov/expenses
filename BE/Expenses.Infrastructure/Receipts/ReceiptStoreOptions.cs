namespace Expenses.Infrastructure.Receipts;

internal sealed class ReceiptStoreOptions
{
    /// <summary>
    /// Where receipt files live. Configured rather than derived, because it is a backup boundary:
    /// the database dump alone is not a complete backup of the ledger any more (D11).
    /// </summary>
    public string RootPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "receipts");
}
