using Expenses.Domain.Entities;

namespace Expenses.Application.Dtos;

/// <summary>
/// What a caller resubmits from a capture response in order to confirm it. The server held none of
/// this: it is exactly what capture returned, echoed back verbatim or edited, since nothing about a
/// capture is retained server-side once the response is sent (D12).
/// </summary>
public sealed record CapturedReceiptCommand(
    Guid TempKey,
    Receipt.ExtractionState State,
    string? FailureReason = null,
    string? SuppliedIkof = null,
    string? SuppliedJikr = null,
    string? ExtractedIkof = null,
    string? ExtractedJikr = null,
    Receipt.FiscalSource FiscalExtractedSource = Receipt.FiscalSource.None,

    /// <summary>
    /// The invoice creation timestamp a fiscal QR decoded, if any — read verbatim from the capture
    /// response, not re-derived. Used only to default the purchase's occurrence when the caller
    /// supplies no date of its own.
    /// </summary>
    string? FiscalCreatedAt = null);
