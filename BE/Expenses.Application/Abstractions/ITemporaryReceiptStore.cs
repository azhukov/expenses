namespace Expenses.Application.Abstractions;

/// <summary>
/// Where a captured image waits until it is confirmed into a purchase or swept away unconfirmed.
/// Distinct from the permanent, content-addressed <see cref="IReceiptImageStore"/>: a temporary
/// file is deleted within a day regardless of whether its bytes match another temporary file, so
/// it is keyed by a fresh <see cref="Guid"/> rather than by content, and two captures of identical
/// bytes never collide.
/// </summary>
/// <summary>A capture just written: the key that addresses it and the content type sniffed from it.</summary>
public sealed record TemporaryCapture(Guid Key, string ContentType);

public interface ITemporaryReceiptStore
{
    /// <summary>
    /// Validates and writes the bytes, returning a key of their own, never reused and never
    /// deduplicated.
    /// </summary>
    Task<TemporaryCapture> Save(byte[] content, CancellationToken cancellationToken = default);

    /// <summary>The bytes behind a key, or null when the key is unknown or already swept away.</summary>
    Task<byte[]?> Read(Guid key, CancellationToken cancellationToken = default);

    /// <summary>Removes a capture. Safe to call for a key that no longer exists.</summary>
    Task Delete(Guid key, CancellationToken cancellationToken = default);

    /// <summary>Keys of captures written before <paramref name="cutoff"/>, for the daily orphan sweep.</summary>
    Task<IReadOnlyList<Guid>> ListOlderThan(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
