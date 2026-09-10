using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Infrastructure.Persistence;

/// <summary>
/// The aggregate root's repository. Expenses come with it and never on their own (D2).
/// </summary>
internal sealed class PurchaseRepository(ExpensesDbContext context) : IPurchaseRepository
{
    public async Task<Purchase?> FindById(long id, CancellationToken cancellationToken = default)
        => await context.Purchases.FirstOrDefaultAsync(purchase => purchase.Id == id, cancellationToken);

    /// <summary>The fast path of the duplicate guard (D4).</summary>
    public async Task<Purchase?> FindByOccurrenceAndAmount(
        DateTime occurredAt,
        decimal amount,
        CancellationToken cancellationToken = default)
        => await context.Purchases.FirstOrDefaultAsync(
            purchase => purchase.OccurredAt == occurredAt && purchase.Amount == amount,
            cancellationToken);

    /// <summary>
    /// Byte-identical receipts share one file, so removing one purchase's receipt must not remove
    /// a file another purchase is still showing (D11). The index on the storage key is what makes
    /// this a lookup rather than a scan.
    /// </summary>
    public async Task<int> CountByReceiptStorageKey(
        string storageKey,
        CancellationToken cancellationToken = default)
        => await context.Purchases.CountAsync(
            purchase => purchase.Receipt!.StorageKey == storageKey,
            cancellationToken);

    /// <summary>
    /// Most recent first, paged. Both bounds are inclusive dates as a user reads them, so the
    /// upper one becomes an exclusive instant at the start of the following day вЂ” a purchase at
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

    public async Task Add(Purchase purchase, CancellationToken cancellationToken = default)
        => await context.Purchases.AddAsync(purchase, cancellationToken);
}
