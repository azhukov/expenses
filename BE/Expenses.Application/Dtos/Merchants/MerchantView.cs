using Expenses.Domain.Entities;

namespace Expenses.Application.Dtos;

/// <summary>
/// A merchant as it is read back. Unlike a category it has no code: its identity is
/// <see cref="TaxId"/> where the receipt printed one, and nothing where it did not (D18).
/// </summary>
public sealed record MerchantView(
    long Id,
    string Name,
    string? TaxId,
    long? ParentId,
    string? ParentName,
    bool IsActive)
{
    public static MerchantView Of(Merchant merchant) => new(
        merchant.Id,
        merchant.Name,
        merchant.TaxId,
        merchant.Parent?.Id ?? merchant.ParentId,
        merchant.Parent?.Name,
        merchant.IsActive);
}
