using System.Collections.Concurrent;
using Expenses.Application.Interfaces;
using Expenses.Domain.Extraction;

namespace Expenses.Infrastructure.Extraction;

/// <summary>
/// Candidates in memory, keyed by purchase (D12). There is no table for them: a candidate exists to
/// be confirmed, edited or discarded, and once confirmed it is an expense — persisting it as well
/// would be a second representation of the same line, plus a cascade and a retention policy for
/// rows nobody accepted.
///
/// What a restart loses is the suggestion; what it keeps is every confirmed expense and the
/// extraction state recorded on the purchase. Absence is therefore an ordinary answer, and the
/// callers report it as absence rather than as a failed extraction.
///
/// A singleton, because the drain and the request that reads the result are different scopes.
/// </summary>
internal sealed class InMemoryExtractionCandidateStore : IExtractionCandidateStore
{
    private readonly ConcurrentDictionary<long, ExtractionResult> _held = new();

    public Task<ExtractionResult?> FindLatest(long purchaseId, CancellationToken cancellationToken = default)
        => Task.FromResult(_held.GetValueOrDefault(purchaseId));

    /// <summary>
    /// A re-run supersedes its predecessor rather than accumulating beside it, and touches no
    /// expense that was already confirmed.
    /// </summary>
    public Task Replace(long purchaseId, ExtractionResult result, CancellationToken cancellationToken = default)
    {
        _held[purchaseId] = result;

        return Task.CompletedTask;
    }

    public Task Discard(long purchaseId, CancellationToken cancellationToken = default)
    {
        _held.TryRemove(purchaseId, out _);

        return Task.CompletedTask;
    }
}
