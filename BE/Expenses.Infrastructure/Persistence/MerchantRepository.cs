using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Infrastructure.Persistence;

/// <summary>
/// Matching is by tax number where present and by name otherwise (D18); searching is accent- and
/// typo-tolerant across both the dictionary and the verbatim text on purchases (D14).
/// </summary>
internal sealed class MerchantRepository(ExpensesDbContext context) : IMerchantRepository
{
    public async Task<Merchant?> FindById(long id, CancellationToken cancellationToken = default)
        => await context.Merchants.FirstOrDefaultAsync(merchant => merchant.Id == id, cancellationToken);

    public async Task<Merchant?> FindByTaxId(string taxId, CancellationToken cancellationToken = default)
        => await context.Merchants.FirstOrDefaultAsync(merchant => merchant.TaxId == taxId, cancellationToken);

    public async Task<Merchant?> FindByName(string name, CancellationToken cancellationToken = default)
        => await context.Merchants.FirstOrDefaultAsync(
            merchant => EF.Functions.ILike(merchant.Name, name),
            cancellationToken);

    public async Task<IReadOnlyList<Merchant>> List(
        bool includeInactive,
        CancellationToken cancellationToken = default)
        => await context.Merchants
            .Where(merchant => includeInactive || merchant.IsActive)
            .OrderBy(merchant => merchant.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MerchantMatch>> Search(
        string term,
        CancellationToken cancellationToken = default)
    {
        // Both sides are stripped of accents, so a name typed without them still finds the merchant
        // it names. Trigram matching tolerates the character-level noise this data has, where
        // stemming would need a language nothing here can reliably detect (D14).
        string pattern = $"%{term}%";

        var dictionary = await context.Merchants
            .Where(merchant => EF.Functions.ILike(
                EF.Functions.Unaccent(merchant.Name),
                EF.Functions.Unaccent(pattern)))
            .OrderBy(merchant => merchant.Name)
            .Select(merchant => new MerchantMatch(merchant, merchant.Name, null))
            .ToListAsync(cancellationToken);

        // An unmatched merchant is exactly the one a user searches for by half-remembered name, so
        // the verbatim text on purchases is searched alongside the dictionary (D9, D14).
        var verbatim = await context.Purchases
            .Where(purchase => purchase.MerchantId == null
                && purchase.MerchantRaw != null
                && EF.Functions.ILike(
                    EF.Functions.Unaccent(purchase.MerchantRaw),
                    EF.Functions.Unaccent(pattern)))
            .OrderByDescending(purchase => purchase.OccurredAt)
            .Select(purchase => new MerchantMatch(null, purchase.MerchantRaw!, purchase.Id))
            .ToListAsync(cancellationToken);

        return [.. dictionary, .. verbatim];
    }

    public async Task<IReadOnlyList<Merchant>> ActiveChildrenOf(
        long merchantId,
        CancellationToken cancellationToken = default)
        => await context.Merchants
            .Where(merchant => merchant.ParentId == merchantId && merchant.IsActive)
            .OrderBy(merchant => merchant.Name)
            .ToListAsync(cancellationToken);

    public async Task Add(Merchant merchant, CancellationToken cancellationToken = default)
        => await context.Merchants.AddAsync(merchant, cancellationToken);
}
