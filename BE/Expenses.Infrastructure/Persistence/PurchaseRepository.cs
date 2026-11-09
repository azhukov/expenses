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

    /// <inheritdoc />
    public async Task<Purchase?> FindLatestByInvoiceCode(string ikof, CancellationToken cancellationToken = default)
        => await context.Purchases
            .Where(purchase => purchase.Fiscal!.FiscalIkofSupplied == ikof
                || purchase.Fiscal!.FiscalIkofExtracted == ikof)
            .OrderByDescending(purchase => purchase.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Most recent first, paged. Both bounds are inclusive dates as a user reads them, so the
    /// upper one becomes an exclusive instant at the start of the following day вЂ” a purchase at
    /// 23:00 on the last day of a month belongs in that month's report.
    /// </summary>
    public async Task<IReadOnlyList<Purchase>> List(
        PurchaseListQuery query,
        CancellationToken cancellationToken = default)
        => await InRange(context.Purchases, query.From, query.To)
            .OrderByDescending(purchase => purchase.OccurredAt)
            .ThenByDescending(purchase => purchase.Id)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>Untracked: an export reads, and nothing it reads is written back.</remarks>
    public async Task<IReadOnlyList<Purchase>> ListForExport(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
        => await InRange(context.Purchases.AsNoTracking(), from, to)
            .OrderBy(purchase => purchase.OccurredAt)
            .ThenBy(purchase => purchase.Id)
            .ToListAsync(cancellationToken);

    public async Task Add(Purchase purchase, CancellationToken cancellationToken = default)
        => await context.Purchases.AddAsync(purchase, cancellationToken);

    /// <summary>
    /// The one statement of the inclusive range, shared by the listing and the export: the upper
    /// date becomes an exclusive instant at the start of the following day, so the last day counts
    /// whole.
    /// </summary>
    private static IQueryable<Purchase> InRange(IQueryable<Purchase> purchases, DateOnly? from, DateOnly? to)
    {
        if (from is { } lower)
        {
            var start = lower.ToDateTime(TimeOnly.MinValue);
            purchases = purchases.Where(purchase => purchase.OccurredAt >= start);
        }

        if (to is { } upper)
        {
            var endExclusive = upper.AddDays(1).ToDateTime(TimeOnly.MinValue);
            purchases = purchases.Where(purchase => purchase.OccurredAt < endExclusive);
        }

        return purchases;
    }

    /// <inheritdoc />
    public async Task<int> Count(CancellationToken cancellationToken = default)
        => await context.Purchases.CountAsync(cancellationToken);

    /// <summary>
    /// One transaction: lock <c>purchases</c> against writes (reads still proceed), count, and either
    /// roll back on a mismatch or read the ids and distinct receipt keys and bulk-delete. The database
    /// cascades <c>expenses</c>; receipt and fiscal data are columns on the row (D2 of
    /// add-clear-all-expenses).
    /// </summary>
    public async Task<PurchaseClearOutcome> DeleteAllIfCountIs(int expected, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // SHARE ROW EXCLUSIVE conflicts with the ROW EXCLUSIVE every insert, update and delete takes,
        // and with itself, but not with reads. A write already in flight is waited for and then
        // counted; one that starts later waits for the commit and lands in the empty ledger.
        await context.Database.ExecuteSqlRawAsync(
            "LOCK TABLE purchases IN SHARE ROW EXCLUSIVE MODE",
            cancellationToken);

        int count = await context.Purchases.CountAsync(cancellationToken);

        if (count != expected)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PurchaseClearOutcome.Mismatch(count);
        }

        var ids = await context.Purchases
            .Select(purchase => purchase.Id)
            .ToListAsync(cancellationToken);

        var storageKeys = await context.Purchases
            .Where(purchase => purchase.Receipt!.StorageKey != null)
            .Select(purchase => purchase.Receipt!.StorageKey)
            .Distinct()
            .ToListAsync(cancellationToken);

        await context.Purchases.ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PurchaseClearOutcome(count, Deleted: true, ids, storageKeys);
    }
}
