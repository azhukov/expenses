using Expenses.Application.Abstractions;
using Expenses.Integration.Tests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Receipts;

/// <summary>
/// Scenarios from receipt-ingestion: "An image can be captured with no purchase behind it", "A
/// captured image is held temporarily until confirmed", "Orphaned captures are removed
/// automatically" — over the real filesystem-backed temporary store, against a root of this test's
/// own, distinct from the permanent receipt store's root.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TemporaryReceiptStoreTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"expenses-temp-receipts-{Guid.NewGuid():n}");

    private ServiceProvider _services = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _services = postgres.Services(("TemporaryReceipts:RootPath", _root));

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
    public async Task Save_returns_a_fresh_key_on_every_call()
    {
        var bytes = Jpeg(0x11);

        var first = await Store.Save(bytes);
        var second = await Store.Save(bytes);

        Assert.NotEqual(first.Key, second.Key);
    }

    [Fact]
    public async Task Saved_bytes_are_read_back()
    {
        var bytes = Jpeg(0x01);

        var capture = await Store.Save(bytes);

        Assert.Equal(bytes, await Store.Read(capture.Key));
    }

    [Fact]
    public async Task Reading_an_unknown_key_is_absence()
    {
        Assert.Null(await Store.Read(Guid.NewGuid()));
    }

    [Fact]
    public async Task Deleting_removes_the_file()
    {
        var capture = await Store.Save(Jpeg(0x09));

        await Store.Delete(capture.Key);

        Assert.Null(await Store.Read(capture.Key));
    }

    [Fact]
    public async Task Deleting_an_already_missing_key_is_not_an_error()
    {
        await Store.Delete(Guid.NewGuid());
    }

    [Fact]
    public async Task ListOlderThan_returns_only_stale_entries()
    {
        var stale = await Store.Save(Jpeg(0x21));
        File.SetLastWriteTimeUtc(PathOf(stale.Key), DateTime.UtcNow.AddDays(-2));

        var fresh = await Store.Save(Jpeg(0x22));

        var older = await Store.ListOlderThan(DateTimeOffset.UtcNow.AddDays(-1));

        Assert.Contains(stale.Key, older);
        Assert.DoesNotContain(fresh.Key, older);
    }

    private ITemporaryReceiptStore Store => _services.GetRequiredService<ITemporaryReceiptStore>();

    private string PathOf(Guid key) =>
        Directory.GetFiles(_root, $"{key:n}*", SearchOption.AllDirectories).Single();

    private static byte[] Jpeg(byte seed) => [0xFF, 0xD8, 0xFF, 0xE0, seed, 0x4A, 0x46, 0x49, 0x46];
}
