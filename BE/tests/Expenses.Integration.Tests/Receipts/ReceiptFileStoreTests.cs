using Expenses.Application.Abstractions;
using Expenses.Application.Errors;
using Expenses.Domain;
using Expenses.Infrastructure.Persistence;
using Expenses.Integration.Tests.Harness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Receipts;

/// <summary>
/// Scenarios from receipt-ingestion: "Uploaded images are validated", "The same bytes are not
/// stored twice", "Receipt bytes are stored as files the purchase refers to", "Stored images can be
/// retrieved" — over the real file store (D11), against a receipt root of this test's own.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReceiptFileStoreTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"expenses-receipts-{Guid.NewGuid():n}");

    private ServiceProvider _services = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _services = postgres.Services(("Receipts:RootPath", _root));

        return postgres.Migrate();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Stored_bytes_are_outside_the_ledger()
    {
        var bytes = Jpeg(0x11);

        var stored = await Store(bytes);

        // The reference is what the ledger will hold; the bytes are on disk under it (D11).
        Assert.Equal(Receipt.ContentHashLength, stored.ContentHash.Length);
        Assert.Equal(bytes.LongLength, stored.SizeInBytes);
        Assert.True(File.Exists(Path.Combine(_root, stored.StorageKey.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task The_layout_is_content_addressed_and_fanned_out()
    {
        var stored = await Store(Jpeg(0x12));

        var hex = Convert.ToHexStringLower(stored.ContentHash);
        Assert.Equal($"{hex[..2]}/{hex[2..4]}/{hex}.jpg", stored.StorageKey);
    }

    [Fact]
    public async Task Identical_bytes_uploaded_again()
    {
        var bytes = Jpeg(0x21);

        var first = await Store(bytes);
        var second = await Store(bytes);

        // One file, because the name is the content: nothing had to look anything up (D11).
        Assert.Equal(first.StorageKey, second.StorageKey);
        Assert.Single(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Re_photographed_receipt()
    {
        var first = await Store(Jpeg(0x31));
        var second = await Store(Jpeg(0x32));

        Assert.NotEqual(first.StorageKey, second.StorageKey);
        Assert.Equal(2, Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Length);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    [InlineData("image/heic")]
    [InlineData("application/pdf")]
    public async Task Accepted_formats_are_detected_from_the_content(string contentType)
    {
        var stored = await Store(SampleOf(contentType));

        Assert.Equal(contentType, stored.ContentType);
    }

    [Fact]
    public async Task Rejected_format()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() =>
            Store("this is a text file, not a receipt"u8.ToArray()));

        Assert.Equal(ApplicationErrors.ReceiptImageUnsupportedFormat, error.Error.Code);
        Assert.Contains("image/jpeg", error.Error.Message, StringComparison.Ordinal);

        // Nothing is written: the rejection happens before a file exists.
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Oversized_file()
    {
        var oversized = new byte[(15 * 1024 * 1024) + 1];
        Jpeg(0x41).CopyTo(oversized, 0);

        var error = await Assert.ThrowsAsync<ExpensesException>(() => Store(oversized));

        Assert.Equal(ApplicationErrors.ReceiptImageTooLarge, error.Error.Code);
        Assert.Contains("15", error.Error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Retrieve_an_image()
    {
        var bytes = Jpeg(0x51);
        var stored = await Store(bytes);

        Assert.Equal(bytes, await Images.Read(stored.StorageKey));
    }

    [Fact]
    public async Task A_referenced_file_that_is_missing_reads_as_absent()
    {
        // Reported as a missing image rather than as a fault: the store is a separate backup
        // boundary from the database, and the reference is still true about what was uploaded (D11).
        Assert.Null(await Images.Read("ab/cd/abcdef.jpg"));
    }

    [Fact]
    public async Task Deleting_removes_the_file()
    {
        var stored = await Store(Jpeg(0x61));

        await Images.Delete(stored.StorageKey);

        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
        Assert.Null(await Images.Read(stored.StorageKey));
    }

    [Fact]
    public async Task Deleting_a_file_that_is_already_gone_is_not_an_error()
    {
        // Deletion runs after the reference is cleared and is best effort by design (D11).
        await Images.Delete("ab/cd/abcdef.jpg");
    }

    [Fact]
    public void The_ledger_carries_no_receipt_bytes()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        // Every type mapped onto the row, because the receipt is an owned value on it (D2, D11).
        var columns = context.Model.GetEntityTypes()
            .Where(entity => entity.GetTableName() == "purchases")
            .SelectMany(entity => entity.GetProperties())
            .Select(property => property.GetColumnName())
            .ToList();

        // The reference is stored; the content never is (D11).
        Assert.Contains("receipt_storage_key", columns);
        Assert.DoesNotContain("Content", columns);
    }

    [Fact]
    public void There_is_no_table_for_images_or_candidates()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        // Nothing an engine proposed is persisted, and a receipt is columns on its purchase rather
        // than a row of its own (D11, D12).
        var tables = context.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .ToList();

        Assert.DoesNotContain("ReceiptImages", tables);
        Assert.DoesNotContain("ExtractionResults", tables);
        Assert.DoesNotContain("ExtractionCandidates", tables);
    }

    private IReceiptImageStore Images => _services.GetRequiredService<IReceiptImageStore>();

    private Task<StoredReceiptFile> Store(byte[] content) => Images.Save(content);

    private static byte[] SampleOf(string contentType) => contentType switch
    {
        "image/jpeg" => Jpeg(0x71),
        "image/png" => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x72, 0x00],
        "image/webp" => [.. "RIFF"u8, 0x20, 0x00, 0x00, 0x00, .. "WEBPVP8 "u8, 0x73],
        "image/heic" => [0x00, 0x00, 0x00, 0x18, .. "ftypheic"u8, 0x00, 0x00, 0x00, 0x00, 0x74],
        "application/pdf" => [.. "%PDF-1.7"u8, 0x0A, 0x75],
        _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, "No sample for this type."),
    };

    /// <summary>JPEG magic bytes, varied by <paramref name="seed"/> so each test owns its hash.</summary>
    private static byte[] Jpeg(byte seed) => [0xFF, 0xD8, 0xFF, 0xE0, seed, 0x4A, 0x46, 0x49, 0x46];
}
