using Expenses.Domain.Entities;

namespace Expenses.Application.Dtos;

/// <summary>
/// A purchase's fiscal invoice as it is read back, beside its image rather than inside it (D35,
/// D41). Identifiers are carried per source with the derived corroboration, because a disagreement
/// is reported rather than resolved (D20); the payload is carried because it is the verification
/// link, and for a purchase read from its code alone it is the only receipt there is.
/// </summary>
public sealed record FiscalInvoiceView(
    string? IkofSupplied,
    string? IkofExtracted,
    string? JikrSupplied,
    string? JikrExtracted,
    FiscalInvoice.FiscalSource ExtractedSource,
    string? Payload,
    FiscalInvoice.FiscalSource PayloadSource,
    FiscalInvoice.FiscalCorroboration Corroboration)
{
    public static FiscalInvoiceView Of(FiscalInvoice fiscal) => new(
        fiscal.FiscalIkofSupplied,
        fiscal.FiscalIkofExtracted,
        fiscal.FiscalJikrSupplied,
        fiscal.FiscalJikrExtracted,
        fiscal.FiscalExtractedSource,
        fiscal.FiscalPayload,
        fiscal.FiscalPayloadSource,
        fiscal.Corroboration);
}
