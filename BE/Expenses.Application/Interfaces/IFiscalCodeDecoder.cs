using Expenses.Application.Dtos;

namespace Expenses.Application.Interfaces;

/// <summary>
/// Decoding a fiscal code from stored bytes. Measured to miss on photographed thermal receipts
/// (D20), so a miss is null rather than an exception, and nothing depends on a hit.
/// </summary>
public interface IFiscalCodeDecoder
{
    Task<FiscalIdentifiers?> Decode(ReceiptImageContent image, CancellationToken cancellationToken = default);
}
