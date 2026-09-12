using Expenses.Application.Dtos;

namespace Expenses.Application.Interfaces;

/// <summary>
/// Candidates are held apart from expenses, so an unconfirmed extraction can exist without the
/// aggregate ever being invalid (D12) — and only in memory, keyed by purchase, so a proposal
/// nobody accepted never becomes part of the ledger. What a restart loses is the suggestion; what
/// it keeps is every expense a person confirmed.
/// </summary>
public interface IExtractionCandidateStore
{
    /// <summary>The result held for a purchase, or null when none is: absence, not an error.</summary>
    Task<ExtractionStepResult?> FindLatest(long purchaseId, CancellationToken cancellationToken = default);

    /// <summary>Replaces any unconfirmed candidates; re-running is non-destructive to expenses.</summary>
    Task Replace(long purchaseId, ExtractionStepResult result, CancellationToken cancellationToken = default);

    Task Discard(long purchaseId, CancellationToken cancellationToken = default);
}
