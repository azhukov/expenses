using Expenses.Application.Abstractions;
using Expenses.Application.Purchases;
using Expenses.Application.Receipts;
using Expenses.Domain;
using Expenses.Infrastructure.Persistence;
using Expenses.Integration.Tests.Harness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Expenses.Integration.Tests.Extraction;

/// <summary>
/// Scenarios from receipt-ingestion: "Extraction lifecycle" and "Candidates are transient and are
/// not part of the ledger" — the upload does not wait for extraction, a receipt left Pending by a
/// restart is picked up again, and the candidates a restart loses are absence rather than a
/// failure (D12).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ExtractionQueueTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTime Occurred = new(2033, 7, 8, 11, 0, 0, DateTimeKind.Unspecified);

    private static int _sequence;

    public Task InitializeAsync() => postgres.Migrate();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_queued_receipt_is_extracted_off_the_request_path()
    {
        await using var services = postgres.Services();
        var (purchaseId, receipt) = await Uploaded(services);

        // The upload left it Pending and queued rather than extracted (D12).
        Assert.Equal(Receipt.ExtractionState.Pending, receipt.State);

        await RunDrain(services, purchaseId);

        Assert.Equal(Receipt.ExtractionState.Extracted, await StateOf(services, purchaseId));
    }

    [Fact]
    public async Task A_receipt_left_pending_by_a_restart_is_swept_up()
    {
        await using var uploading = postgres.Services();
        var (purchaseId, _) = await Uploaded(uploading);

        // A second composition root is a restart as far as the in-process queue is concerned: what
        // it had queued is gone, and only the persisted Pending state remains.
        await using var restarted = postgres.Services();

        await RunDrain(restarted, purchaseId);

        Assert.Equal(Receipt.ExtractionState.Extracted, await StateOf(restarted, purchaseId));
    }

    /// <summary>
    /// The other half of a restart: the extraction state is durable and the candidates are not
    /// (D12). Reading reports absence with the recorded state, and does not start extraction.
    /// </summary>
    [Fact]
    public async Task Candidates_do_not_outlive_a_restart()
    {
        await using var extracting = postgres.Services();
        var (purchaseId, _) = await Uploaded(extracting);
        await RunDrain(extracting, purchaseId);

        using (var scope = extracting.CreateScope())
        {
            var held = await scope.ServiceProvider.GetRequiredService<GetExtractionCandidates>()
                .Execute(purchaseId);

            Assert.True(held.CandidatesHeld);
            Assert.NotEmpty(held.Result!.Candidates);
        }

        await using var restarted = postgres.Services();
        using var afterRestart = restarted.CreateScope();

        var view = await afterRestart.ServiceProvider.GetRequiredService<GetExtractionCandidates>()
            .Execute(purchaseId);

        Assert.False(view.CandidatesHeld);
        Assert.Null(view.Result);
        Assert.Equal(Receipt.ExtractionState.Extracted, view.Receipt.State);
    }

    /// <summary>
    /// Starts the real background service and waits for it to reach the receipt, rather than calling
    /// the use case directly — the drain and the sweep are what is under test.
    /// </summary>
    private static async Task RunDrain(IServiceProvider services, long purchaseId)
    {
        var drain = services.GetServices<IHostedService>().OfType<BackgroundService>().Single();
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await drain.StartAsync(stopping.Token);

        try
        {
            while (await StateOf(services, purchaseId) is Receipt.ExtractionState.Pending
                   or Receipt.ExtractionState.Extracting)
            {
                stopping.Token.ThrowIfCancellationRequested();
                await Task.Delay(50, stopping.Token);
            }
        }
        finally
        {
            await drain.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<Receipt.ExtractionState> StateOf(IServiceProvider services, long purchaseId)
    {
        using var scope = services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ExpensesDbContext>()
            .Purchases
            .AsNoTracking()
            .Where(purchase => purchase.Id == purchaseId)
            .Select(purchase => purchase.Receipt!.State)
            .SingleAsync();
    }

    private static async Task<(long PurchaseId, ReceiptView Receipt)> Uploaded(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var occurred = Occurred.AddMinutes(Interlocked.Increment(ref _sequence));

        var purchase = await scope.ServiceProvider.GetRequiredService<RecordPurchase>()
            .Execute(new RecordPurchaseCommand(occurred, 4.00m, [new ExpenseCommand("Line", 4.00m)]));

        var receipt = await scope.ServiceProvider.GetRequiredService<AttachReceiptImage>()
            .Execute(purchase.Purchase.Id, Jpeg((byte)(0x80 + _sequence)));

        return (purchase.Purchase.Id, receipt);
    }

    private static byte[] Jpeg(byte seed) =>
        [0xFF, 0xD8, 0xFF, 0xE0, seed, 0x4A, 0x46, 0x49, 0x46, 0x00, seed, 0x02];
}
