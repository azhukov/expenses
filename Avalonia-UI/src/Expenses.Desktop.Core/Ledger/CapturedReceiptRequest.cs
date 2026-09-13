namespace Expenses.Desktop.Core.Ledger;

/// <summary>
/// What a client resubmits from a capture response in order to confirm it. The server holds nothing
/// about a capture, so none of this may be re-derived from what the user edited.
/// </summary>
public sealed record CapturedReceiptRequest(
    string TempKey,
    ExtractionState State,
    string? FailureReason,
    string? Jikr,
    FiscalSource FiscalSource,
    string? FiscalPayload);
