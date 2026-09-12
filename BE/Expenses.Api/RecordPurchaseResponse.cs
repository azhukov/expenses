using Expenses.Application.Dtos;

namespace Expenses.Api;

/// <summary>
/// A recorded purchase, with <see cref="AlreadyRecorded"/> beside the status code so a client that
/// only reads the body can still tell the two outcomes apart (D3).
/// </summary>
public sealed record RecordPurchaseResponse(
    long Id,
    DateTime OccurredAt,
    decimal Amount,
    long? MerchantId,
    string? MerchantRaw,
    bool HasReceipt,
    IReadOnlyList<ExpenseView> Expenses,
    decimal TotalSaving,
    decimal? SavingPercentage,
    bool AlreadyRecorded,
    MerchantView? Merchant,
    bool MerchantNewlyAdded)
{
    public static RecordPurchaseResponse Of(RecordPurchaseResult result) => new(
        result.Purchase.Id,
        result.Purchase.OccurredAt,
        result.Purchase.Amount,
        result.Purchase.MerchantId,
        result.Purchase.MerchantRaw,
        result.Purchase.HasReceipt,
        result.Purchase.Expenses,
        result.Purchase.TotalSaving,
        result.Purchase.SavingPercentage,
        result.AlreadyRecorded,
        result.Merchant,
        result.MerchantNewlyAdded);
}
