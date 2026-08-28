using Expenses.Domain;

namespace Expenses.Application.Abstractions;

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
    /// How many purchases carry a receipt with these bytes. Byte-identical receipts share one file
    /// in the store, so deleting one purchase's receipt must not delete a file another is still
    /// showing (D11).
    /// </summary>
    Task<int> CountByReceiptContentHash(byte[] contentHash, CancellationToken cancellationToken = default);

    /// <summary>Most recent first, paged. Ordering belongs here because the database does it.</summary>
    Task<IReadOnlyList<Purchase>> List(PurchaseListQuery query, CancellationToken cancellationToken = default);

    Task Add(Purchase purchase, CancellationToken cancellationToken = default);
}
