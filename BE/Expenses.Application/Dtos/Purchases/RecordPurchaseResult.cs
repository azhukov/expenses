namespace Expenses.Application.Dtos;

/// <summary>
/// Distinguishes a newly created purchase from one that was already recorded (D3). Both are
/// successes; an assistant that receives an error routes around it, one that receives
/// "already recorded, here it is" simply proceeds.
/// </summary>
public sealed record RecordPurchaseResult(
    PurchaseView Purchase,
    bool AlreadyRecorded,
    MerchantView? Merchant = null,
    bool MerchantNewlyAdded = false)
{
    /// <summary>
    /// Values that were not kept, and why. Only a confirmed capture has any: a value extraction read
    /// that the confirming person could not see, and that the ledger refuses, is dropped and named
    /// here rather than refusing the purchase.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
