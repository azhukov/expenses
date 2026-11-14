using Expenses.Application.Dtos;

namespace Expenses.Application.Interfaces;

/// <summary>
/// Retrieving the whole invoice from the fiscal verification service the decoded code refers to.
///
/// A miss is no invoice, exactly as a decode miss is: the service having no record, refusing, or
/// never answering are all a step that produced nothing, and none of them is a problem with the
/// receipt (D26). Which of them it was is still classified and returned alongside, so that the
/// capture can say why (D43). The identity the service answers with travels on the result itself,
/// because the JIKR arrives with the invoice and is knowable no other way (D24, D34).
/// </summary>
public interface IFiscalInvoiceRetrieval
{
    Task<FiscalRetrieval> Retrieve(
        long purchaseId,
        FiscalIdentifiers decoded,
        CancellationToken cancellationToken = default);
}
