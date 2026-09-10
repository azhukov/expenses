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
    bool MerchantNewlyAdded = false);
