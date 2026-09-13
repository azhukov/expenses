namespace Expenses.Desktop.Core.Screens;

/// <summary>One recent purchase, ready to show: no identifier survives to this shape.</summary>
public sealed record RecentPurchaseRow(string Merchant, string When, string Lines, bool HasReceipt, string Amount);
