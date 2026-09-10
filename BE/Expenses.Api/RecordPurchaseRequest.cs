namespace Expenses.Api;

/// <summary>
/// <see cref="OccurredAt"/> may be omitted only when <see cref="Capture"/> is supplied and its
/// fiscal QR decoded an invoice creation timestamp (D5).
/// </summary>
public sealed record RecordPurchaseRequest(
    decimal Amount,
    IReadOnlyList<ExpenseRequest> Expenses,
    MerchantRequest? Merchant = null,
    DateTime? OccurredAt = null,
    CapturedReceiptRequest? Capture = null);
