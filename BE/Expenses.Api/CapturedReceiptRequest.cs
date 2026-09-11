using Expenses.Application.Dtos;
using Expenses.Domain.Entities;

namespace Expenses.Api;

/// <summary>
/// What a client resubmits from a capture response in order to confirm it — the temporary key,
/// and the extraction outcome exactly as capture reported it (D12: nothing about a capture is held
/// server-side, so this is the only way the server learns it again).
/// </summary>
public sealed record CapturedReceiptRequest(
    Guid TempKey,
    Receipt.ExtractionState State,
    string? FailureReason = null,
    string? Jikr = null,
    Receipt.FiscalSource FiscalSource = Receipt.FiscalSource.None,
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
