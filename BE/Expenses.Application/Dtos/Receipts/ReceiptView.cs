using Expenses.Domain.Entities;

namespace Expenses.Application.Dtos;

/// <summary>
/// A receipt as it is read back. It has no identifier of its own: a receipt is addressed by the
/// purchase that carries it (D11). The fiscal identifiers are carried per source with the derived
/// corroboration beside them, because a disagreement is reported rather than resolved (D20).
///
/// The image and the fiscal invoice are read from the purchase, which owns both (D35); the image's
/// content type and size are absent where the purchase was read from its fiscal code alone.
/// </summary>
public sealed record ReceiptView(
    string? ContentType,
    long? SizeInBytes,
    Purchase.ExtractionState State,
    string? FailureReason,
    string? FiscalIkofSupplied,
    string? FiscalIkofExtracted,
    string? FiscalJikrSupplied,
    string? FiscalJikrExtracted,
    FiscalInvoice.FiscalSource FiscalExtractedSource,
    FiscalInvoice.FiscalCorroboration Corroboration)
{
    /// <summary>Only for a purchase that carries a receipt: its extraction state is what this reports.</summary>
    public static ReceiptView Of(Purchase purchase, Purchase.ExtractionState state) => new(
        purchase.Receipt?.ContentType,
        purchase.Receipt?.SizeInBytes,
        state,
        purchase.ExtractionFailureReason,
        purchase.Fiscal?.FiscalIkofSupplied,
        purchase.Fiscal?.FiscalIkofExtracted,
        purchase.Fiscal?.FiscalJikrSupplied,
        purchase.Fiscal?.FiscalJikrExtracted,
        purchase.Fiscal?.FiscalExtractedSource ?? FiscalInvoice.FiscalSource.None,
        purchase.Fiscal?.Corroboration ?? FiscalInvoice.FiscalCorroboration.Absent);
}
