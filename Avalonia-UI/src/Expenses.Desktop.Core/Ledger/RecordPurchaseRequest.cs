namespace Expenses.Desktop.Core.Ledger;

/// <summary>
/// <see cref="OccurredAt"/> may be null only when <see cref="Capture"/> is supplied and its fiscal
/// code decoded an invoice creation timestamp; the API rejects the request otherwise.
/// </summary>
public sealed record RecordPurchaseRequest(
    decimal Amount,
    IReadOnlyList<ExpenseRequest> Expenses,
    MerchantRequest? Merchant,
    DateTime? OccurredAt,
    CapturedReceiptRequest? Capture);
