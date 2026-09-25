namespace Expenses.Application.Dtos;

/// <summary>
/// What every extraction step is given: the image, and the fiscal identity the run has established
/// so far. Identical for every step, so a step cannot depend on where in the run it sits (D28).
///
/// <paramref name="Fiscal"/> arrives from the client at capture, or from the step before this one,
/// and is <see cref="FiscalIdentifiers.None"/> when nothing is known yet. <paramref name="Payload"/>
/// is the fiscal QR payload behind it, where one was supplied or already decoded — a step that has
/// this has no reason to decode the image again (D31).
///
/// <paramref name="Image"/> is absent when a payload was captured on its own, or a purchase read from
/// one is re-run; only steps that do not read the image are reached then (D37).
/// </summary>
public sealed record ExtractionStepRequest(
    ReceiptImageContent? Image,
    FiscalIdentifiers Fiscal,
    string? Payload = null)
{
    /// <summary>
    /// The purchase the result will describe. Purely descriptive — nothing is looked up by it — and
    /// zero for a capture no purchase exists behind yet.
    /// </summary>
    public long PurchaseId { get; init; } = Image?.PurchaseId ?? 0;
}
