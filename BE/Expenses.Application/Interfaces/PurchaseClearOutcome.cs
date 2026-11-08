namespace Expenses.Application.Interfaces;

/// <summary>
/// What a conditional clear found and did. <see cref="CountBefore"/> is the number of purchases the
/// ledger held when the clear ran. When <see cref="Deleted"/> is false nothing was removed and both
/// lists are empty; when it is true they name what went, so the caller can discard candidates and
/// dispose of receipt files after the commit.
/// </summary>
public sealed record PurchaseClearOutcome(
    int CountBefore,
    bool Deleted,
    IReadOnlyList<long> PurchaseIds,
    IReadOnlyList<string> ReceiptStorageKeys)
{
    public static PurchaseClearOutcome Mismatch(int countBefore) => new(countBefore, false, [], []);
}
