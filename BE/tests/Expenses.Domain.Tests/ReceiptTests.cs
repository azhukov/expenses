using Expenses.Domain.Entities;

namespace Expenses.Domain.Tests;

/// <summary>
/// The receipt image alone: a reference to a stored file. What extraction made of it, and the fiscal
/// invoice it may have carried, belong to the purchase (D35) and are covered by
/// <see cref="PurchaseReceiptTests"/>.
/// </summary>
public sealed class ReceiptTests
{
    private static Receipt AnImage() => Receipt.Of("ab/cd/abcd.jpg", "image/jpeg", sizeInBytes: 2_000_000);

    [Fact]
    public void A_receipt_requires_the_storage_key_of_its_file()
    {
        var error = Assert.Throws<ArgumentException>(() => Receipt.Of("  ", "image/jpeg", sizeInBytes: 1));

        Assert.Equal("storageKey", error.ParamName);
    }

    [Fact]
    public void A_receipt_requires_content()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Receipt.Of("ab/cd/abcd.jpg", "image/jpeg", sizeInBytes: 0));

        Assert.Equal("sizeInBytes", error.ParamName);
    }

    [Fact]
    public void The_storage_key_is_retained_as_written()
    {
        // Stored rather than recomputed from the content, so the layout of the store can change
        // without invalidating existing references (D11).
        Assert.Equal("ab/cd/abcd.jpg", AnImage().StorageKey);
    }
}
