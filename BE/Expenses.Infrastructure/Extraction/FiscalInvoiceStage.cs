using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Expenses.Infrastructure.Extraction;

/// <summary>
/// The deterministic extractor (D22): where the code decoded, the whole invoice is asked for from
/// the service that issued it, and what comes back is authoritative rather than estimated. It leads
/// the cascade, so no probabilistic stage is ever asked for a value this has already established.
///
/// Its failures are all the same failure: no code decoded, no record, no answer, or an answer in a
/// shape nobody has seen. Every one of them is a stage that produced nothing, and the placeholder
/// stages behind it run exactly as they would for a receipt carrying no code at all (D26).
/// </summary>
internal sealed class FiscalInvoiceStage(
    IFiscalInvoiceRetrieval retrieval,
    ILogger<FiscalInvoiceStage> logger) : IExtractionStage
{
    public string Name => FiscalPortalClient.Stage;

    public ExtractionStageRole Role => ExtractionStageRole.Primary;

    public async Task<ExtractionStageOutcome> Run(
        ExtractionStageRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var invoice = await retrieval.Retrieve(request.Image.PurchaseId, request.Known, cancellationToken);

            return invoice is null
                ? ExtractionStageOutcome.Nothing
                : new ExtractionStageOutcome(
                    invoice.Result,
                    invoice.Identifiers,
                    Receipt.FiscalSource.RetrievedFromService);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(
                exception,
                "The invoice for the receipt of purchase {PurchaseId} could not be retrieved; continuing without it.",
                request.Image.PurchaseId);

            return ExtractionStageOutcome.Nothing;
        }
    }
}
