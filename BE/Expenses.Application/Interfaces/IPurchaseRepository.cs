using Expenses.Domain.Entities;

namespace Expenses.Application.Interfaces;

/// <summary>A date range and a page of it. Both bounds are inclusive, as a user reads them.</summary>
public sealed record PurchaseListQuery(DateOnly? From, DateOnly? To, int Skip, int Take);

/// <summary>
/// <see cref="Purchase"/> is the only aggregate root, so it is the only entity with a
/// repository (D2).
/// </summary>
public interface IPurchaseRepository
{
    Task<Purchase?> FindById(long id, CancellationToken cancellationToken = default);

    /// <summary>The fast path of the duplicate guard (D4).</summary>
    Task<Purchase?> FindByOccurrenceAndAmount(
        DateTime occurredAt,
        decimal amount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many purchases carry a receipt stored under this key. The store is content-addressed, so
    /// byte-identical receipts share one file, and deleting one purchase's receipt must not delete a
    /// file another is still showing (D11).
    /// </summary>
    Task<int> CountByReceiptStorageKey(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The most recently occurred purchase whose fiscal invoice carries this invoice identification
    /// code, supplied or extracted, compared exactly. Null where none does. Asked at every capture so
    /// that a receipt scanned twice is warned about rather than silently recorded twice (D39).
    /// </summary>
    Task<Purchase?> FindLatestByInvoiceCode(string ikof, CancellationToken cancellationToken = default);

    /// <summary>Most recent first, paged. Ordering belongs here because the database does it.</summary>
    Task<IReadOnlyList<Purchase>> List(PurchaseListQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every purchase in an inclusive occurrence date range, unpaged, with its expenses: oldest first,
    /// and by identifier among purchases at the same moment, so they come out in the order they were
    /// recorded. Either bound may be absent. An export reads a range whole, which is why this is not
    /// <see cref="List"/> with a large page (D2 of add-csv-export).
    /// </summary>
    Task<IReadOnlyList<Purchase>> ListForExport(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default);

    Task Add(Purchase purchase, CancellationToken cancellationToken = default);

    /// <summary>How many purchases the ledger holds, across every date.</summary>
    Task<int> Count(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every purchase, with its expense lines, receipt reference and fiscal identity, but only
    /// when the ledger holds exactly <paramref name="expected"/> of them. The count and the delete are
    /// one transaction that no other write can interleave with, so a purchase recorded after the
    /// caller counted is never deleted unseen. On a mismatch nothing is deleted and the outcome
    /// carries the count found. This is the one write that commits on its own rather than through
    /// <see cref="IUnitOfWork"/>, because the bulk delete bypasses tracking and the lock has to
    /// cover the count (D2 of add-clear-all-expenses).
    /// </summary>
    Task<PurchaseClearOutcome> DeleteAllIfCountIs(int expected, CancellationToken cancellationToken = default);
}
