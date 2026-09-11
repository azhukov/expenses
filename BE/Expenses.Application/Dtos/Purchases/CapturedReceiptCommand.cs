using Expenses.Domain.Entities;

namespace Expenses.Application.Dtos;

/// <summary>
/// What a caller resubmits from a capture response in order to confirm it. The server held none of
/// this: it is exactly what capture returned, echoed back verbatim, since nothing about a capture is
/// retained server-side once the response is sent (D12).
///
/// Three fiscal members where there were six. The payload yields the invoice code, the issuer tax
/// number, the creation timestamp and the total, so none of those needs its own field; what the
/// payload cannot yield is the JIKR — which only the verification service knows (D24) — and how the
/// identity was established (D32).
/// </summary>
public sealed record CapturedReceiptCommand(
    Guid TempKey,
    Receipt.ExtractionState State,
    string? FailureReason = null,

    /// <summary>Absent from the fiscal code and printed nowhere the server can read it (D24).</summary>
    string? Jikr = null,

    Receipt.FiscalSource FiscalSource = Receipt.FiscalSource.None,

    /// <summary>
    /// The receipt's fiscal QR payload, verbatim, exactly as the capture response carried it.
    /// Retained on the receipt so a later extraction reads it rather than decoding the photograph
    /// again at the same one-in-three odds, and so an unrecognised format stays recoverable (D32).
    /// </summary>
    string? FiscalPayload = null);
