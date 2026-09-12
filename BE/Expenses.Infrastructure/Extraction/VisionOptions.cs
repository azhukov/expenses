namespace Expenses.Infrastructure.Extraction;

internal sealed class VisionOptions
{
    /// <summary>The Claude model asked to read the receipt image.</summary>
    public string ModelId { get; set; } = "claude-sonnet-5";

    /// <summary>
    /// Never committed: supplied via environment or user-secrets. Empty means the stage is not
    /// configured to run at all, which is an ordinary outcome (D32), not a startup failure.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Configured rather than compiled in so a test can point it somewhere that is not the real API (D26).</summary>
    public string BaseAddress { get; set; } = "https://api.anthropic.com";

    /// <summary>
    /// Bounded, because ingestion may never wait indefinitely on a model response. When it runs out
    /// the stage produced nothing, which is an outcome the cascade already handles everywhere.
    /// </summary>
    public int TimeoutMilliseconds { get; set; } = 30000;
}
