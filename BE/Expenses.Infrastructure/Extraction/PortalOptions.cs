namespace Expenses.Infrastructure.Extraction;

internal sealed class PortalOptions
{
    /// <summary>
    /// The one national fiscalisation portal every receipt in this design is assumed to be verified
    /// by. Configured rather than compiled in so a test can point it somewhere that is not a
    /// government service (D26).
    /// </summary>
    public string BaseAddress { get; set; } = "https://mapr.tax.gov.me";

    /// <summary>
    /// Bounded, because ingestion may never wait on someone else's server. When it runs out the
    /// stage produced nothing, which is an outcome the cascade already handles everywhere.
    /// </summary>
    public int TimeoutMilliseconds { get; set; } = 5000;
}
