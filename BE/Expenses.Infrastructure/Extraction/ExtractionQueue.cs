using System.Threading.Channels;
using Expenses.Application.Abstractions;
using Expenses.Application.Receipts;
using Expenses.Domain;
using Expenses.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Expenses.Infrastructure.Extraction;

internal sealed class QueueOptions
{
    /// <summary>
    /// Bounded on purpose: work that cannot be queued is work the sweep will find still Pending
    /// after a restart, which is preferable to unbounded memory (D12).
    /// </summary>
    public int Capacity { get; set; } = 256;
}

internal sealed class ExtractionHostingOptions
{
    /// <summary>
    /// Whether this process drains the queue. On in every real host; off where something else
    /// drives extraction deliberately — a test that runs the cascade itself would otherwise be
    /// racing the drain for the same images.
    /// </summary>
    public bool DrainInBackground { get; set; } = true;
}

/// <summary>
/// Extraction runs off the request path (D12). An in-process bounded channel is enough for a
/// single-user ledger; the cost is that queued-but-unrun work is lost on restart, which is what the
/// startup sweep exists to repair. A durable queue is the obvious upgrade if that changes.
/// </summary>
internal sealed class ExtractionQueue(QueueOptions options) : IExtractionQueue
{
    private readonly Channel<long> _queue = Channel.CreateBounded<long>(
        new BoundedChannelOptions(options.Capacity) { FullMode = BoundedChannelFullMode.Wait });

    public ValueTask Enqueue(long purchaseId, CancellationToken cancellationToken = default) =>
        _queue.Writer.WriteAsync(purchaseId, cancellationToken);

    public IAsyncEnumerable<long> Drain(CancellationToken cancellationToken) =>
        _queue.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Drains the queue, one image at a time, in a scope of its own. It also performs the startup
/// sweep: anything left Pending by a restart is queued again, so nothing is stranded (D12).
/// </summary>
internal sealed class ExtractionService(
    ExtractionQueue queue,
    IServiceScopeFactory scopes,
    ILogger<ExtractionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Sweep(stoppingToken);

        await foreach (var purchaseId in queue.Drain(stoppingToken))
        {
            await Extract(purchaseId, stoppingToken);
        }
    }

    /// <summary>
    /// Pending is persisted precisely so that a restart loses nothing permanently: whatever was
    /// queued but not run is found here and queued again (D12).
    /// </summary>
    private async Task Sweep(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

            // Only Pending and a receipt stranded mid-extraction. A receipt whose candidates simply
            // evaporated with the process is not swept: candidates are transient by design, and
            // re-running is an explicit act rather than something a restart pays for (D12).
            var pending = await context.Purchases
                .Where(purchase => purchase.Receipt!.State == Receipt.ExtractionState.Pending
                    || purchase.Receipt!.State == Receipt.ExtractionState.Extracting)
                .Select(purchase => purchase.Id)
                .ToListAsync(cancellationToken);

            foreach (var purchaseId in pending)
            {
                await queue.Enqueue(purchaseId, cancellationToken);
            }

            if (pending.Count > 0)
            {
                logger.LogInformation("Re-queued {Count} receipts left unextracted by a restart.", pending.Count);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A sweep that cannot run must not stop the drain: new uploads still deserve extracting,
            // and the images it would have found stay Pending for the next start.
            logger.LogError(exception, "The startup sweep for pending receipt images failed.");
        }
    }

    private async Task Extract(long purchaseId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<RunExtraction>()
                .Execute(purchaseId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // One image failing is not the drain failing. The image keeps whatever state it
            // reached, and a user can re-run it.
            logger.LogError(exception, "Extraction failed for the receipt of purchase {PurchaseId}.", purchaseId);
        }
    }
}
