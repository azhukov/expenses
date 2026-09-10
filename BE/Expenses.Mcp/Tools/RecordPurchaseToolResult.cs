using Expenses.Application.Dtos;

namespace Expenses.Mcp.Tools;

/// <summary>
/// What recording produced. <see cref="Summary"/> is the sentence an assistant relays: a repeated
/// call is a success that says so, not an error it would try to route around (D3).
/// </summary>
public sealed record RecordPurchaseToolResult(
    string Summary,
    bool AlreadyRecorded,
    bool MerchantNewlyAdded,
    PurchaseView Purchase)
{
    public static RecordPurchaseToolResult Of(RecordPurchaseResult result)
    {
        string merchant = result.Merchant is null
            ? string.Empty
            : $" at {result.Merchant.Name}"
              + (result.MerchantNewlyAdded ? ", newly added to the merchant list" : string.Empty);

        string summary = result.AlreadyRecorded
            ? $"This purchase was already recorded: {result.Purchase.Amount} on "
              + $"{result.Purchase.OccurredAt:yyyy-MM-dd HH:mm}, identifier {result.Purchase.Id}. "
              + "Nothing was changed."
            : $"Recorded {result.Purchase.Amount} on {result.Purchase.OccurredAt:yyyy-MM-dd HH:mm}"
              + $"{merchant}, identifier {result.Purchase.Id}.";

        return new RecordPurchaseToolResult(
            summary,
            result.AlreadyRecorded,
            result.MerchantNewlyAdded,
            result.Purchase);
    }
}
