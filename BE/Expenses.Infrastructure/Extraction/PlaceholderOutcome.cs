namespace Expenses.Infrastructure.Extraction;

/// <summary>
/// What the placeholder is told to produce, so that every terminal state the pipeline can reach is
/// reachable before a real engine exists (D12). A simulated outcome is configuration, not a guess
/// the engine makes about itself.
/// </summary>
internal enum PlaceholderOutcome
{
    /// <summary>Numbers that add up: the cascade validates them and the image is Extracted.</summary>
    Reconciling = 0,

    /// <summary>
    /// Numbers that do not add up. The failure is real arithmetic rather than a simulated score,
    /// which is what makes it exercise the fallback for the reason a real engine would (D20).
    /// </summary>
    NonReconciling = 1,

    /// <summary>Sound numbers with a description the engine is unsure of — the review path.</summary>
    LowConfidence = 2,

    /// <summary>No result at all.</summary>
    Failure = 3,
}
