using Expenses.Application.Dtos;

namespace Expenses.Application.Interfaces;

/// <summary>
/// Retrieving the whole invoice from the fiscal verification service the decoded code refers to.
///
/// A miss is null, exactly as a decode miss is: the service having no record, refusing, or never
/// answering are all a stage that produced nothing, and none of them is a problem with the
/// receipt (D26).
/// </summary>
public interface IFiscalInvoiceRetrieval
{
    Task<RetrievedInvoice?> Retrieve(
        long purchaseId,
        FiscalIdentifiers decoded,
        CancellationToken cancellationToken = default);
}
