using Expenses.Domain.Entities;

namespace Expenses.Application.Dtos;

/// <summary>
/// A receipt as it is read back. It has no identifier of its own: a receipt is addressed by the
/// purchase that carries it (D11). The fiscal identifiers are carried per source with the derived
/// corroboration beside them, because a disagreement is reported rather than resolved (D20).
/// </summary>
public sealed record ReceiptView(
    string ContentType,
    long SizeInBytes,
    Receipt.ExtractionState State,
    string? FailureReason,
    string? FiscalIkofSupplied,
    string? FiscalIkofExtracted,
    string? FiscalJikrSupplied,
    string? FiscalJikrExtracted,
    Receipt.FiscalSource FiscalExtractedSource,
    Receipt.FiscalCorroboration Corroboration)
{
    public static ReceiptView Of(Receipt receipt) => new(
        receipt.ContentType,
        receipt.SizeInBytes,
        receipt.State,
        receipt.FailureReason,
        receipt.FiscalIkofSupplied,
        receipt.FiscalIkofExtracted,
        receipt.FiscalJikrSupplied,
        receipt.FiscalJikrExtracted,
        receipt.FiscalExtractedSource,
        receipt.Corroboration);
}
