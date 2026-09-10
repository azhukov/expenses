using System.ComponentModel;
using Expenses.Application.Dtos;
using Expenses.Domain.Entities;

namespace Expenses.Mcp.Tools;

/// <summary>
/// What a capture reported, resubmitted to confirm it. Nothing about a capture is held server-side
/// (D12), so an assistant carries this forward exactly as the capture tool returned it вЂ” or edited,
/// where the extraction result needed correcting.
/// </summary>
public sealed record CapturedReceiptArgument(
    [property: Description("The temporary key the capture response returned.")]
    Guid TempKey,
    [property: Description("The extraction outcome the capture response reported: Extracted, NeedsReview or Failed.")]
    Receipt.ExtractionState State,
    [property: Description("Why extraction failed, when the state is Failed.")]
    string? FailureReason = null,
    [property: Description("The IKOF fiscal identifier supplied at capture, if any.")]
    string? SuppliedIkof = null,
    [property: Description("The JIKR fiscal identifier supplied at capture, if any.")]
    string? SuppliedJikr = null,
    [property: Description("The IKOF fiscal identifier extraction read from the image, if any.")]
    string? ExtractedIkof = null,
    [property: Description("The JIKR fiscal identifier extraction read from the image, if any.")]
    string? ExtractedJikr = null,
    [property: Description("How the extracted fiscal identifiers were obtained, as capture reported it.")]
    Receipt.FiscalSource FiscalExtractedSource = Receipt.FiscalSource.None,
    [property: Description(
        "The invoice creation timestamp a fiscal QR decoded, if any, as capture reported it. Used to "
        + "default the purchase's date when none is supplied.")]
    string? FiscalCreatedAt = null)
{
    public CapturedReceiptCommand ToCommand() => new(
        TempKey,
        State,
        FailureReason,
        SuppliedIkof,
        SuppliedJikr,
        ExtractedIkof,
        ExtractedJikr,
        FiscalExtractedSource,
        FiscalCreatedAt);
}
