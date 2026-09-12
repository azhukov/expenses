using Expenses.Application.Interfaces;

namespace Expenses.Infrastructure.Receipts;

/// <summary>
/// Captured bytes held under a GUID-keyed filename until confirmed or swept away (D-none вЂ” new for
/// this change). No content addressing and no deduplication: a temporary file is deleted within a
/// day regardless of whether its bytes match another temporary file, and two captures of identical
/// bytes must never collide while one is still being confirmed.
/// </summary>
internal sealed class TemporaryReceiptFileStore(TemporaryReceiptStoreOptions options) : ITemporaryReceiptStore
{
    /// <summary>Fails loudly at startup rather than at the first capture, mirroring the permanent store.</summary>
    public static void Verify(TemporaryReceiptStoreOptions options)
    {
        string root = options.RootPath;

        try
        {
            Directory.CreateDirectory(root);

            string probe = Path.Combine(root, $".write-probe-{Environment.ProcessId}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The temporary receipt store at '{root}' is missing or cannot be written to. Captured images "
                + "wait here until confirmed, so the ledger will not start without it.",
                exception);
        }
    }

    public async Task<TemporaryCapture> Save(byte[] content, CancellationToken cancellationToken = default)
    {
        string contentType = ReceiptContent.Validate(content);

        var key = Guid.NewGuid();
        Directory.CreateDirectory(options.RootPath);
        await File.WriteAllBytesAsync(Resolve(key, contentType), content, cancellationToken);

        return new TemporaryCapture(key, contentType);
    }

    public async Task<byte[]?> Read(Guid key, CancellationToken cancellationToken = default)
    {
        string? path = PathFor(key);

        return path is null ? null : await File.ReadAllBytesAsync(path, cancellationToken);
    }

    public Task Delete(Guid key, CancellationToken cancellationToken = default)
    {
        string? path = PathFor(key);
        if (path is not null)
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> ListOlderThan(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(options.RootPath))
        {
            return Task.FromResult<IReadOnlyList<Guid>>([]);
        }

        var stale = Directory.EnumerateFiles(options.RootPath)
            .Where(path => File.GetLastWriteTimeUtc(path) < cutoff.UtcDateTime)
            .Select(path => KeyOf(Path.GetFileNameWithoutExtension(path)))
            .Where(key => key is not null)
            .Select(key => key!.Value)
            .ToList();

        return Task.FromResult<IReadOnlyList<Guid>>(stale);
    }

    private string Resolve(Guid key, string contentType)
        => Path.Combine(options.RootPath, $"{key:n}{Extension(contentType)}");

    /// <summary>The extension carries no meaning beyond a human glancing at the directory.</summary>
    private static string Extension(string contentType) => contentType switch
    {
        ReceiptContent.Jpeg => ".jpg",
        ReceiptContent.Png => ".png",
        ReceiptContent.WebP => ".webp",
        ReceiptContent.Heic => ".heic",
        ReceiptContent.Pdf => ".pdf",
        _ => ".bin",
    };

    private string? PathFor(Guid key) => Directory.Exists(options.RootPath)
        ? Directory.EnumerateFiles(options.RootPath, $"{key:n}.*").FirstOrDefault()
        : null;

    private static Guid? KeyOf(string fileNameWithoutExtension)
        => Guid.TryParse(fileNameWithoutExtension, out var key) ? key : null;
}
