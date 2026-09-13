namespace Expenses.Desktop.Core.Ledger;

/// <summary>A merchant as free text, matched or created server-side.</summary>
public sealed record MerchantRequest(string Text, string? TaxId);
