using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Application.Services;
using Expenses.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Expenses.Infrastructure.Extraction;

/// <summary>
/// The deterministic step: establish the receipt's fiscal identity, then ask the verification
/// service for the invoice behind it (D22, D28).
///
/// Decoding is not a step of its own. It never produced lines and nothing but this consumed what it
/// found, so it was an argument to the portal call rather than a stage of the run — and folding it
/// in is what removes the channel stages used to exchange data through (D29).
///
/// Every way this fails is the same failure: no payload, no identifiers, no record, no answer, or
/// an answer in a shape nobody has seen. Each is a step that read no lines, and the vision step
/// behind it runs exactly as it would for a receipt carrying no code at all (D26). Where a payload
/// was obtained but the invoice was not, what was decoded still travels forward — a known-true
/// total and issuer identity are worth having even when the portal is not (D23).
/// </summary>
internal sealed class FiscalStep(
    IFiscalCodeDecoder decoder,
    IFiscalInvoiceRetrieval retrieval,
    ILogger<FiscalStep> logger) : IExtractionStep
{
    public const string StepName = "fiscal";

    public string Name => StepName;

    public async Task<ExtractionStepResult?> Run(
        ExtractionStepRequest request,
        CancellationToken cancellationToken = default)
    {
        var (payload, source) = await Payload(request, cancellationToken);

        if (payload is null)
        {
            return null;
        }

        var identifiers = request.Fiscal.IsEmpty ? FiscalIdentity.From(payload) : request.Fiscal;

        if (identifiers.IsEmpty)
        {
            // A payload nothing could be read from is still worth carrying: it is retained on the
            // receipt so that a parser which later learns this format can read it (D32).
            return ExtractionStepResult.FiscalOnly(Name, identifiers, source, payload);
        }

        var invoice = await Retrieve(request.Image.PurchaseId, identifiers, cancellationToken);

        return invoice is null
            ? ExtractionStepResult.FiscalOnly(Name, identifiers, source, payload)
            : invoice.WithPayload(payload);
    }

    /// <summary>
    /// The payload the run already holds, or one decoded from the image. A supplied payload is
    /// preferred outright and the image is not read a second time: a client reading a live camera
    /// has focus and retries available to it that a single stored frame does not, and the server's
    /// own decoder is measured at one hit in three (D31).
    /// </summary>
    private async Task<(string? Payload, Receipt.FiscalSource Source)> Payload(
        ExtractionStepRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Payload is { Length: > 0 })
        {
            return (request.Payload, Receipt.FiscalSource.SuppliedAtUpload);
        }

        try
        {
            return (await decoder.Decode(request.Image, cancellationToken), Receipt.FiscalSource.DecodedFromCode);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A decoder that throws is a decoder that missed. Nothing depends on a hit, so a
            // failure here must not become a failure of the extraction, an error the user sees, or
            // a reason for the step behind this one to behave differently (D20).
            logger.LogDebug(
                exception,
                "Fiscal code decoding failed for the receipt of purchase {PurchaseId}; continuing without it.",
                request.Image.PurchaseId);

            return (null, Receipt.FiscalSource.None);
        }
    }

    private async Task<ExtractionStepResult?> Retrieve(
        long purchaseId,
        FiscalIdentifiers identifiers,
        CancellationToken cancellationToken)
    {
        try
        {
            return await retrieval.Retrieve(purchaseId, identifiers, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(
                exception,
                "The invoice for the receipt of purchase {PurchaseId} could not be retrieved; continuing without it.",
                purchaseId);

            return null;
        }
    }
}