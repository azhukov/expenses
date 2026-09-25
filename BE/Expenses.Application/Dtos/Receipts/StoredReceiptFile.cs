using Expenses.Domain.Entities;

namespace Expenses.Application.Dtos;

/// <summary>
/// A file that is on disk. Constructing a <see cref="Receipt"/> from this is what records the
/// reference, and it happens after the write, so a purchase can never point at a file that was
/// never written (D11).
/// </summary>
public sealed record StoredReceiptFile(string StorageKey, string ContentType, long SizeInBytes)
{
    public Receipt AsReceipt()
        => Receipt.Of(StorageKey, ContentType, SizeInBytes);
}
