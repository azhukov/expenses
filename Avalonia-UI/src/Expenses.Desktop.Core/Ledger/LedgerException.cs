namespace Expenses.Desktop.Core.Ledger;

/// <summary>
/// A failure talking to the ledger, carrying the ledger's own words where it gave any. The client
/// has no error vocabulary of its own: replacing a specific message with a generic one is how a user
/// ends up told "something went wrong" about a problem the API had already named.
/// </summary>
public sealed class LedgerException(string message, string? code = null, int? status = null, string? correlationId = null)
    : Exception(message)
{
    /// <summary>The API's error code, or null where the failure never reached the API.</summary>
    public string? Code { get; } = code;

    /// <summary>The HTTP status, or null where no response was received.</summary>
    public int? Status { get; } = status;

    public string? CorrelationId { get; } = correlationId;
}
