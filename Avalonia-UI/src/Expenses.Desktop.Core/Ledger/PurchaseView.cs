namespace Expenses.Desktop.Core.Ledger;

/// <summary>
/// A purchase as it is read back. <see cref="MerchantRaw"/> travels beside <see cref="MerchantId"/>
/// always, because a later match must never erase what was printed, so the display name is resolved
/// on the client. <see cref="OccurredAt"/> carries no offset: it is wall-clock time and is never
/// converted through a time zone.
/// </summary>
public sealed record PurchaseView(
    long Id,
    DateTime OccurredAt,
    decimal Amount,
    long? MerchantId,
    string? MerchantRaw,
    bool HasReceipt,
    IReadOnlyList<ExpenseView> Expenses,
    decimal TotalSaving,
    decimal? SavingPercentage,
    ExtractionState? ExtractionState);
