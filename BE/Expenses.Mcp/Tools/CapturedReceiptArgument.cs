using System.ComponentModel;
using Expenses.Application.Dtos;
using Expenses.Domain.Entities;

namespace Expenses.Mcp.Tools;

/// <summary>
/// What a capture reported, resubmitted to confirm it. Nothing about a capture is held server-side
/// (D12), so an assistant carries this forward exactly as the capture tool returned it — or edited,
/// where the extraction result needed correcting.
///
/// Three fiscal members where there were six: the payload states the invoice code, the issuer tax
/// number, the creation timestamp and the total, so an assistant hands back the string it was given
/// rather than fields parsed out of it (D30, D32).
/// </summary>
public sealed record CapturedReceiptArgument(
    [property: Description("The temporary key the capture response returned.")]
    Guid TempKey,
    [property: Description("The extraction outcome the capture response reported: Extracted, NeedsReview or Failed.")]
    Receipt.ExtractionState State,
    [property: Description("Why extraction failed, when the state is Failed.")]
    string? FailureReason = null,
    [property: Description(
        "The JIKR fiscal identifier, if any. It is absent from the fiscal code and answered only by "
        + "the verification service, so it cannot be read back out of the payload.")]
    string? Jikr = null,
    [property: Description("How the fiscal identity was established, as capture reported it.")]
    Receipt.FiscalSource FiscalSource = Receipt.FiscalSource.None,
    [property: Description(
        "The receipt's fiscal QR payload verbatim, as capture reported it. The invoice code, issuer "
        + "tax number, creation timestamp and total are read from it server-side; the timestamp is "
        + "what defaults the purchase's date when none is supplied.")]
    string? FiscalPayload = null)
{
    public CapturedReceiptCommand ToCommand() => new(
        TempKey,
        State,
        FailureReason,
        Jikr,
        FiscalSource,
        FiscalPayload);
}
