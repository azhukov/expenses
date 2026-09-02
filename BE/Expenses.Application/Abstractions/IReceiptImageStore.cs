using Expenses.Domain;

namespace Expenses.Application.Abstractions;

/// <summary>The bytes of one receipt, loaded deliberately and never incidentally (D11).</summary>
public sealed record ReceiptImageContent(long PurchaseId, string ContentType, byte[] Content);

/// <summary>
/// A file that is on disk. Constructing a <see cref="Receipt"/> from this is what records the
/// reference, and it happens after the write, so a purchase can never point at a file that was
/// never written (D11).
/// </summary>
public sealed record StoredReceiptFile(byte[] ContentHash, string StorageKey, string ContentType, long SizeInBytes)
{
    public Receipt AsReceipt(Receipt.ExtractionState state, string? failureReason = null) =>
        Receipt.Of(ContentHash, StorageKey, ContentType, SizeInBytes, state, failureReason);
}

/// <summary>
/// The receipt store: files under a configured root, referenced from the purchase (D11). This port
/// is what keeps a later move to object storage contained to one adapter. Content type is
/// determined from the content by the adapter, never from a declared type or an extension.
/// </summary>
public interface IReceiptImageStore
{
    /// <summary>
    /// Writes the bytes and returns the reference to them. Content addressing means identical
    /// bytes resolve to one file: a second write of the same content is a no-op, and two purchases
    /// may share the file without either knowing.
    /// </summary>
    Task<StoredReceiptFile> Save(byte[] content, CancellationToken cancellationToken = default);

    /// <summary>
    /// The bytes behind a stored reference, or null when the file is not there. A missing file is
    /// reported as a missing image rather than as a fault, because the ledger row is still true
    /// about what was uploaded (D11).
    /// </summary>
    Task<byte[]?> Read(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a file. Called only after the reference is cleared, and only when nothing else
    /// references the same content: byte-identical receipts share one file (D11).
    /// </summary>
    Task Delete(string storageKey, CancellationToken cancellationToken = default);
}
