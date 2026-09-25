using Expenses.Application.Dtos;
using Expenses.Domain.Entities;

namespace Expenses.Api;

/// <summary>
/// What a client resubmits from a capture response in order to confirm it — the temporary key of an
/// image capture, or the payload of a fiscal one (D38),
/// and the extraction outcome exactly as capture reported it (D12: nothing about a capture is held
/// server-side, so this is the only way the server learns it again).
/// </summary>
public sealed record CapturedReceiptRequest(
    Guid? TempKey,
    Purchase.ExtractionState State,
    string? FailureReason = null,
    string? Jikr = null,
    FiscalInvoice.FiscalSource FiscalSource = FiscalInvoice.FiscalSource.None,
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
