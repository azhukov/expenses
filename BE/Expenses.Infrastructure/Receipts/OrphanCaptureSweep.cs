using Expenses.Application.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Expenses.Infrastructure.Receipts;

/// <summary>
/// Removes temporary captures nobody confirmed within a day. The one piece of background
/// processing this change keeps: unlike the extraction queue it replaces, there is no per-item
/// retry or ordering to get right, only "old enough, so gone" (D-none вЂ” new for this change).
/// </summary>
internal sealed class OrphanCaptureSweep(
    ITemporaryReceiptStore tempStore,
    OrphanCaptureSweepOptions options,
    ILogger<OrphanCaptureSweep> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Interval);

        do
        {
            await SweepOnce(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// One run of the sweep, callable directly so a test can exercise it without waiting on the
    /// timer. Confirming a key the sweep already removed is reported the same as any unknown key
    /// (D-none) вЂ” nothing here needs to know about confirmation at all.
    /// </summary>
    public async Task SweepOnce(CancellationToken cancellationToken)
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow - options.MaxAge;
            var stale = await tempStore.ListOlderThan(cutoff, cancellationToken);

            foreach (var key in stale)
            {
                await tempStore.Delete(key, cancellationToken);
            }

            if (stale.Count > 0)
            {
                logger.LogInformation("Swept {Count} unconfirmed temporary captures.", stale.Count);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A sweep that fails once must not stop future ones: an orphaned file left an extra
            // day is a cost, not a correctness problem.
            logger.LogError(exception, "The daily orphan capture sweep failed.");
        }
    }
}
