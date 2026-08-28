namespace Expenses.Application.Abstractions;

/// <summary>
/// Extraction runs off the request path (D12). Queued work is in-process and bounded; a purchase
/// whose receipt is still <c>Pending</c> after a restart is re-queued by a startup sweep.
/// </summary>
public interface IExtractionQueue
{
    ValueTask Enqueue(long purchaseId, CancellationToken cancellationToken = default);
}
