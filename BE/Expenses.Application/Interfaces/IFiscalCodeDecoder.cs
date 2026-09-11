using Expenses.Application.Dtos;

namespace Expenses.Application.Interfaces;

/// <summary>
/// Decoding a fiscal code from stored bytes. Measured to miss on photographed thermal receipts
/// (D20), so a miss is null rather than an exception, and nothing depends on a hit.
///
/// It yields the payload the symbol carries and nothing more. Reading identifiers out of that
/// payload is <see cref="Services.FiscalIdentity"/>'s work, so that a payload a client supplied and
/// a payload decoded here are understood by one implementation rather than two (D30).
/// </summary>
public interface IFiscalCodeDecoder
{
    Task<string?> Decode(ReceiptImageContent image, CancellationToken cancellationToken = default);
}
