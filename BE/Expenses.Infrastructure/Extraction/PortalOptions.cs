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
    ///
    /// Twenty seconds, because the portal is slow rather than unreachable. Measured on 2026-11-14,
    /// it answered verifyInvoice in a steady 9.4 to 9.6 s with a 0.05 s connect. The earlier 5 s
    /// budget timed out on every receipt while the same lookup worked in a browser, which waits.
    /// </summary>
    public int TimeoutMilliseconds { get; set; } = 20000;
}
