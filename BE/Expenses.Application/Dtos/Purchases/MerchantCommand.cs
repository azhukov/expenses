namespace Expenses.Application.Dtos;

/// <summary>
/// Where the purchase was made, as printed. <paramref name="TaxId"/> is the authoritative match
/// where a receipt carried one (D18).
/// </summary>
public sealed record MerchantCommand(string Text, string? TaxId = null);
