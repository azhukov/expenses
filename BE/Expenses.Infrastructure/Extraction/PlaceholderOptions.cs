namespace Expenses.Infrastructure.Extraction;

internal sealed class PlaceholderOptions
{
    public PlaceholderOutcome Outcome { get; set; } = PlaceholderOutcome.Reconciling;

    /// <summary>Reported for values arithmetic cannot decide when simulating low confidence.</summary>
    public decimal LowConfidenceValue { get; set; } = 0.35m;
}
