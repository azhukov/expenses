using Expenses.Application.Interfaces;
using Expenses.Integration.Tests.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Expenses.Integration.Tests.Receipts;

/// <summary>
/// Scenarios from receipt-ingestion: "Orphaned captures are removed automatically" — over the real
/// hosted sweep, wired exactly as a host would run it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrphanCaptureSweepTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"expenses-sweep-{Guid.NewGuid():n}");

    private ServiceProvider _services = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _services = postgres.Services(
            ("TemporaryReceipts:RootPath", _root),
            ("TemporaryReceipts:Sweep:Interval", "00:00:00.050"),
            ("TemporaryReceipts:Sweep:MaxAge", "-00:00:01"));

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
    public async Task An_old_unconfirmed_capture_is_swept_away()
    {
        var store = _services.GetRequiredService<ITemporaryReceiptStore>();
        var capture = await store.Save(Jpeg(0x81));

        var hostedServices = _services.GetServices<IHostedService>().ToList();
        foreach (var service in hostedServices)
        {
            await service.StartAsync(CancellationToken.None);
        }

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (await store.Read(capture.Key) is not null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.Null(await store.Read(capture.Key));
        }
        finally
        {
            foreach (var service in hostedServices)
            {
                await service.StopAsync(CancellationToken.None);
            }
        }
    }

    private static byte[] Jpeg(byte seed) => [0xFF, 0xD8, 0xFF, 0xE0, seed, 0x4A, 0x46, 0x49, 0x46];
}
