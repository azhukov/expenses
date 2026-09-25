namespace Expenses.Domain.Entities;

/// <summary>
/// The receipt image a purchase carries: the reference to its stored file. A value inside the
/// aggregate rather than an entity beside it (D2, D11) — the bytes live in a file under the receipt
/// store, and this holds the identity of that file, never its content.
///
/// What extraction made of the image, and the fiscal invoice it may have carried, belong to the
/// purchase rather than to this (D35): a purchase read from its fiscal code alone has both and no
/// image, and deleting an image must not take them with it.
/// </summary>
public sealed class Receipt
{
    public const int StorageKeyMaxLength = 256;

    private Receipt()
    {
        // EF materialisation.
        StorageKey = null!;
        ContentType = null!;
    }

    /// <summary>
    /// Where the file is, relative to the receipt store, and with it the identity of the file's
    /// content: the store is content-addressed, so byte-identical receipts resolve to one key and
    /// two purchases carrying the same key are the same file. Retained as written rather than
    /// recomputed, so the layout of the store can change without rewriting history (D11).
    /// </summary>
    public string StorageKey { get; private set; }

    /// <summary>Determined from the file content, never from the declared type or extension.</summary>
    public string ContentType { get; private set; }

    public long SizeInBytes { get; private set; }

    /// <summary>
    /// The reference to a file already written to the store. Constructed only after the bytes are
    /// on disk, so a purchase can never point at a file that was never written (D11).
    /// </summary>
    public static Receipt Of(string storageKey, string contentType, long sizeInBytes)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException(
                "A receipt requires the storage key of the file holding its bytes.",
                nameof(storageKey));
        }

        if (storageKey.Length > StorageKeyMaxLength)
        {
            throw new ArgumentException(
                $"A storage key is at most {StorageKeyMaxLength} characters.",
                nameof(storageKey));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException(
                "A receipt requires a content type determined from its content.",
                nameof(contentType));
        }

        if (sizeInBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sizeInBytes),
                sizeInBytes,
                "A receipt must have content.");
        }

        return new Receipt
        {
            StorageKey = storageKey.Trim(),
            ContentType = contentType.Trim(),
            SizeInBytes = sizeInBytes,
        };
    }
}
