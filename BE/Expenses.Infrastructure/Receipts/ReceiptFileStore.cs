using System.Security.Cryptography;
using Expenses.Application.Abstractions;

namespace Expenses.Infrastructure.Receipts;

internal sealed class ReceiptStoreOptions
{
    /// <summary>
    /// Where receipt files live. Configured rather than derived, because it is a backup boundary:
    /// the database dump alone is not a complete backup of the ledger any more (D11).
    /// </summary>
    public string RootPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "receipts");
}

/// <summary>
/// Receipt bytes in files, referenced from the purchase (D11). Content-addressed:
/// <c>&lt;root&gt;/&lt;aa&gt;/&lt;bb&gt;/&lt;full hex&gt;&lt;extension&gt;</c>, fanned out two levels
/// so no directory grows unbounded, with the extension taken from the sniffed content type rather
/// than from anything the uploader claimed.
///
/// Identical bytes therefore resolve to one path and the second write is a no-op — deduplication is
/// a property of the naming rather than of a database index, which is why two purchases may share
/// one file and why deleting one of them must ask whether the other is still there.
/// </summary>
internal sealed class ReceiptFileStore(ReceiptStoreOptions options) : IReceiptImageStore
{
    private static readonly Dictionary<string, string> s_extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [ReceiptContent.Jpeg] = ".jpg",
        [ReceiptContent.Png] = ".png",
        [ReceiptContent.WebP] = ".webp",
        [ReceiptContent.Heic] = ".heic",
        [ReceiptContent.Pdf] = ".pdf",
    };

    /// <summary>
    /// Fails loudly at startup rather than at the first upload: a store that cannot be written to
    /// loses images, and that is not something the ledger can correct at runtime (D11, D13).
    /// </summary>
    public static void Verify(ReceiptStoreOptions options)
    {
        string root = options.RootPath;

        try
        {
            Directory.CreateDirectory(root);

            // Creating the directory proves nothing about being able to write into it — a
            // read-only mount answers the first and refuses the second.
            string probe = Path.Combine(root, $".write-probe-{Environment.ProcessId}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The receipt store at '{root}' is missing or cannot be written to. Receipt images are files "
                + "referenced from the ledger (D11), so the ledger will not start without it.",
                exception);
        }
    }

    public async Task<StoredReceiptFile> Save(byte[] content, CancellationToken cancellationToken = default)
    {
        string contentType = ReceiptContent.Validate(content);

        byte[] hash = SHA256.HashData(content);
        string storageKey = StorageKey(hash, contentType);
        string path = Resolve(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // The same bytes are the same file: a re-upload writes nothing and the caller gets the
        // reference it would have got the first time.
        if (!File.Exists(path))
        {
            await WriteAtomically(path, content, cancellationToken);
        }

        return new StoredReceiptFile(hash, storageKey, contentType, content.LongLength);
    }

    public async Task<byte[]?> Read(string storageKey, CancellationToken cancellationToken = default)
    {
        string path = Resolve(storageKey);

        return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
    }

    /// <summary>
    /// Best effort by design: the reference is already gone, so a file that survives is
    /// unreferenced garbage rather than a broken link, and failing here must not fail the
    /// operation that removed the reference (D11).
    /// </summary>
    public Task Delete(string storageKey, CancellationToken cancellationToken = default)
    {
        try
        {
            File.Delete(Resolve(storageKey));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left for collection.
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Written under a temporary name and renamed, so a reader never sees a half-written file and
    /// an interrupted write leaves nothing that looks like a stored receipt.
    /// </summary>
    private static async Task WriteAtomically(string path, byte[] content, CancellationToken cancellationToken)
    {
        string temporary = $"{path}.{Guid.NewGuid():n}.tmp";

        try
        {
            await File.WriteAllBytesAsync(temporary, content, cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    /// <summary>
    /// Content-addressed and stored verbatim on the purchase, so this layout can change later
    /// without rewriting a single existing reference (D11).
    /// </summary>
    private static string StorageKey(byte[] hash, string contentType)
    {
        string hex = Convert.ToHexStringLower(hash);

        return $"{hex[..2]}/{hex[2..4]}/{hex}{s_extensions.GetValueOrDefault(contentType, ".bin")}";
    }

    private string Resolve(string storageKey)
        => Path.Combine(options.RootPath, storageKey.Replace('/', Path.DirectorySeparatorChar));
}
