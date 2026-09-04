using Expenses.Application.Abstractions;
using Expenses.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Expenses.Infrastructure.Persistence;

/// <summary>
/// The aggregate root's repository. Expenses come with it and never on their own (D2).
/// </summary>
internal sealed class PurchaseRepository(ExpensesDbContext context) : IPurchaseRepository
{
    public async Task<Purchase?> FindById(long id, CancellationToken cancellationToken = default) =>
        await context.Purchases.FirstOrDefaultAsync(purchase => purchase.Id == id, cancellationToken);

    /// <summary>The fast path of the duplicate guard (D4).</summary>
    public async Task<Purchase?> FindByOccurrenceAndAmount(
        DateTime occurredAt,
        decimal amount,
        CancellationToken cancellationToken = default) =>
        await context.Purchases.FirstOrDefaultAsync(
            purchase => purchase.OccurredAt == occurredAt && purchase.Amount == amount,
            cancellationToken);

    /// <summary>
    /// Byte-identical receipts share one file, so removing one purchase's receipt must not remove
    /// a file another purchase is still showing (D11). The index on the hash is what makes this a
    /// lookup rather than a scan.
    /// </summary>
    public async Task<int> CountByReceiptContentHash(
        byte[] contentHash,
        CancellationToken cancellationToken = default) =>
        await context.Purchases.CountAsync(
            purchase => purchase.Receipt!.ContentHash == contentHash,
            cancellationToken);

    /// <summary>
    /// Most recent first, paged. Both bounds are inclusive dates as a user reads them, so the
    /// upper one becomes an exclusive instant at the start of the following day — a purchase at
    /// 23:00 on the last day of a month belongs in that month's report.
    /// </summary>
    public async Task<IReadOnlyList<Purchase>> List(
        PurchaseListQuery query,
        CancellationToken cancellationToken = default)
    {
        var purchases = context.Purchases.AsQueryable();

        if (query.From is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue);
            purchases = purchases.Where(purchase => purchase.OccurredAt >= start);
        }

        if (query.To is { } to)
        {
            var endExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
            purchases = purchases.Where(purchase => purchase.OccurredAt < endExclusive);
        }

        return await purchases
            .OrderByDescending(purchase => purchase.OccurredAt)
            .ThenByDescending(purchase => purchase.Id)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken);
    }

    public async Task Add(Purchase purchase, CancellationToken cancellationToken = default) =>
        await context.Purchases.AddAsync(purchase, cancellationToken);
}

internal sealed class CategoryRepository(ExpensesDbContext context) : ICategoryRepository
{
    public async Task<Category?> FindById(long id, CancellationToken cancellationToken = default) =>
        await context.Categories.FirstOrDefaultAsync(category => category.Id == id, cancellationToken);

    /// <summary>
    /// Codes are ASCII and uppercase by convention, but a caller that types one in lower case is
    /// naming the same entry, so the comparison is case-insensitive rather than the caller having
    /// to know the convention (D8).
    /// </summary>
    public async Task<Category?> FindByCode(string code, CancellationToken cancellationToken = default) =>
        await context.Categories.FirstOrDefaultAsync(
            category => EF.Functions.ILike(category.Code, code),
            cancellationToken);

    public async Task<IReadOnlyList<Category>> List(
        bool includeInactive,
        CancellationToken cancellationToken = default) =>
        await context.Categories
            .Where(category => includeInactive || category.IsActive)
            .OrderBy(category => category.Code)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Category>> ActiveChildrenOf(
        long categoryId,
        CancellationToken cancellationToken = default) =>
        await context.Categories
            .Where(category => category.ParentId == categoryId && category.IsActive)
            .OrderBy(category => category.Code)
            .ToListAsync(cancellationToken);

    public async Task Add(Category category, CancellationToken cancellationToken = default) =>
        await context.Categories.AddAsync(category, cancellationToken);

    public Task Remove(Category category, CancellationToken cancellationToken = default)
    {
        context.Categories.Remove(category);
        return Task.CompletedTask;
    }
}

internal sealed class UnitRepository(ExpensesDbContext context) : IUnitRepository
{
    public async Task<Unit?> FindById(long id, CancellationToken cancellationToken = default) =>
        await context.Units.FirstOrDefaultAsync(unit => unit.Id == id, cancellationToken);

    public async Task<Unit?> FindByCode(string code, CancellationToken cancellationToken = default) =>
        await context.Units.FirstOrDefaultAsync(unit => EF.Functions.ILike(unit.Code, code), cancellationToken);

    public async Task<IReadOnlyList<Unit>> List(
        bool includeInactive,
        CancellationToken cancellationToken = default) =>
        await context.Units
            .Where(unit => includeInactive || unit.IsActive)
            .OrderBy(unit => unit.Code)
            .ToListAsync(cancellationToken);
}

/// <summary>
/// Matching is by tax number where present and by name otherwise (D18); searching is accent- and
/// typo-tolerant across both the dictionary and the verbatim text on purchases (D14).
/// </summary>
internal sealed class MerchantRepository(ExpensesDbContext context) : IMerchantRepository
{
    public async Task<Merchant?> FindById(long id, CancellationToken cancellationToken = default) =>
        await context.Merchants.FirstOrDefaultAsync(merchant => merchant.Id == id, cancellationToken);

    public async Task<Merchant?> FindByTaxId(string taxId, CancellationToken cancellationToken = default) =>
        await context.Merchants.FirstOrDefaultAsync(merchant => merchant.TaxId == taxId, cancellationToken);

    public async Task<Merchant?> FindByName(string name, CancellationToken cancellationToken = default) =>
        await context.Merchants.FirstOrDefaultAsync(
            merchant => EF.Functions.ILike(merchant.Name, name),
            cancellationToken);

    public async Task<IReadOnlyList<Merchant>> List(
        bool includeInactive,
        CancellationToken cancellationToken = default) =>
        await context.Merchants
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
        var pattern = $"%{term}%";

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
        CancellationToken cancellationToken = default) =>
        await context.Merchants
            .Where(merchant => merchant.ParentId == merchantId && merchant.IsActive)
            .OrderBy(merchant => merchant.Name)
            .ToListAsync(cancellationToken);

    public async Task Add(Merchant merchant, CancellationToken cancellationToken = default) =>
        await context.Merchants.AddAsync(merchant, cancellationToken);
}

/// <summary>
/// One transaction per use case (D16), and the place a unique-index violation becomes the duplicate
/// outcome the guard expects rather than an infrastructure exception surfacing through it (D4).
/// </summary>
internal sealed class ExpensesUnitOfWork(ExpensesDbContext context) : IUnitOfWork
{
    private const string DuplicatePurchaseIndex = "ix_purchases_occurred_at_amount";

    public async Task SaveChanges(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: DuplicatePurchaseIndex,
            })
        {
            var losing = exception.Entries
                .Select(entry => entry.Entity)
                .OfType<Purchase>()
                .FirstOrDefault();

            // Detached so that a caller re-querying for the winner is not handed the row the
            // database has just refused from the change tracker.
            if (losing is not null)
            {
                context.Entry(losing).State = EntityState.Detached;
            }

            throw new DuplicatePurchaseException(
                losing?.OccurredAt ?? default,
                losing?.Amount ?? default,
                exception);
        }
    }
}
