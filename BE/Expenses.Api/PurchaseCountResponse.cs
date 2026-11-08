namespace Expenses.Api;

/// <summary>How many purchases the ledger holds, across every date.</summary>
public sealed record PurchaseCountResponse(int Count);
