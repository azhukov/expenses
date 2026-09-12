namespace Expenses.Application.Errors;

/// <summary>
/// Raised when the unique index on <c>(occurred_at, amount)</c> rejects an insert (D4). The
/// persistence adapter translates the provider's unique-violation into this, so the duplicate
/// guard is expressed in one place — the use case — rather than leaking an infrastructure
/// exception upward or reimplementing the guard downward.
/// </summary>
public sealed class DuplicatePurchaseException(DateTime occurredAt, decimal amount, Exception? innerException = null) : Exception($"A purchase occurring {occurredAt:O} for {amount} is already recorded.", innerException)
{
    public DateTime OccurredAt { get; } = occurredAt;

    public decimal Amount { get; } = amount;
}
