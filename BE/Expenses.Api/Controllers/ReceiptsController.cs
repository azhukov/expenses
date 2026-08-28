using Expenses.Application.Abstractions;
using Expenses.Application.Errors;
using Expenses.Application.Extraction;
using Expenses.Application.Purchases;
using Expenses.Application.Receipts;
using Microsoft.AspNetCore.Mvc;

namespace Expenses.Api.Controllers;

/// <summary>
/// Receipts and the extraction that reads them. A receipt belongs to exactly one purchase and has
/// no identifier of its own (D11), so every route here hangs off the purchase. As with every
/// adapter action, binding and shaping is all that happens (D1).
/// </summary>
[ApiController]
[Route("purchases/{id:long}")]
public sealed class ReceiptsController : ControllerBase
{
    /// <summary>Enforced before the body is read, so an oversized upload is never buffered.</summary>
    public const long MaximumUploadBytes = 15 * 1024 * 1024;

    /// <summary>Attaches a receipt image to a purchase, with any fiscal identifiers decoded at capture.</summary>
    [HttpPost("receipt")]
    [Produces("application/json")]
    [EndpointName("UploadReceiptImage")]
    public async Task<ActionResult<ReceiptView>> Upload(
        long id,
        [FromServices] AttachReceiptImage attach,
        CancellationToken cancellationToken)
    {
        var form = await Request.ReadFormAsync(cancellationToken);
        var file = form.Files["file"]
            ?? throw ExpensesException.For(
                ApplicationErrors.ReceiptImageUnsupportedFormat,
                "The upload carried no file part named 'file'.");

        // Checked from the declared length before a byte is copied: the limit exists to stop
        // the request being buffered, so enforcing it after buffering would miss the point.
        if (file.Length > MaximumUploadBytes)
        {
            throw ExpensesException.For(
                ApplicationErrors.ReceiptImageTooLarge,
                $"A receipt image may be at most {MaximumUploadBytes / (1024 * 1024)} MB.",
                ("sizeInBytes", file.Length),
                ("maximumSizeInBytes", MaximumUploadBytes));
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        // Identifiers a client decoded at capture, accepted without extraction having run (D20).
        var supplied = new FiscalIdentifiers(form["fiscalIkof"], form["fiscalJikr"]);

        var receipt = await attach.Execute(id, buffer.ToArray(), supplied, cancellationToken);

        // Located by the purchase, because that is the only way a receipt is addressed (D11).
        return Created($"/purchases/{id}/receipt", receipt);
    }

    /// <summary>Returns the stored bytes of a purchase's receipt with its content type.</summary>
    [HttpGet("receipt/content")]
    [EndpointName("DownloadReceiptImage")]
    public async Task<ActionResult> Download(
        long id,
        [FromServices] IPurchaseRepository purchases,
        [FromServices] IReceiptImageStore images,
        CancellationToken cancellationToken)
    {
        var purchase = await purchases.FindById(id, cancellationToken)
            ?? throw ExpensesException.For(
                ApplicationErrors.PurchaseNotFound,
                $"There is no purchase with identifier {id}.",
                ("id", id));

        var receipt = purchase.Receipt
            ?? throw ExpensesException.For(
                ApplicationErrors.ReceiptImageNotFound,
                $"Purchase {id} has no receipt image.",
                ("purchaseId", id));

        // A file that is not there is a missing image, not a server fault: the row is still true
        // about what was uploaded, and the store is a separate backup boundary (D11).
        var content = await images.Read(receipt.StorageKey, cancellationToken)
            ?? throw ExpensesException.For(
                ApplicationErrors.ReceiptImageNotFound,
                $"The stored file for the receipt of purchase {id} is missing.",
                ("purchaseId", id),
                ("storageKey", receipt.StorageKey));

        return File(content, receipt.ContentType);
    }

    /// <summary>Removes the receipt of a purchase, leaving the expenses already confirmed from it.</summary>
    [HttpDelete("receipt")]
    [Produces("application/json")]
    [EndpointName("DeleteReceiptImage")]
    public async Task<ActionResult<PurchaseView>> Delete(
        long id,
        [FromServices] DeleteReceipt delete,
        CancellationToken cancellationToken) => Ok(await delete.Execute(id, cancellationToken));

    /// <summary>Returns a receipt's extraction state, candidates, stage provenance and arithmetic checks.</summary>
    [HttpGet("extraction")]
    [Produces("application/json")]
    [EndpointName("GetExtraction")]
    public async Task<ActionResult<ExtractionResponse>> GetExtraction(
        long id,
        [FromServices] GetExtractionCandidates candidates,
        CancellationToken cancellationToken) => Ok(ExtractionResponse.Of(
            await candidates.Execute(id, cancellationToken)));

    /// <summary>Turns candidate lines — as extracted or as edited — into the expenses of the purchase.</summary>
    [HttpPost("extraction/confirm")]
    [Produces("application/json")]
    [EndpointName("ConfirmCandidates")]
    public async Task<ActionResult<PurchaseView>> Confirm(
        long id,
        [FromBody] ConfirmCandidatesRequest? request,
        [FromServices] ConfirmCandidates confirm,
        CancellationToken cancellationToken)
    {
        RequestGuards.RejectDiscountPercentage(request?.Expenses);

        var edited = request?.Expenses?.Select(expense => expense.ToCommand()).ToList();

        return Ok(await confirm.Execute(id, edited, cancellationToken));
    }

    /// <summary>Removes a receipt's candidate lines, leaving the receipt and the purchase.</summary>
    [HttpPost("extraction/discard")]
    [Produces("application/json")]
    [EndpointName("DiscardCandidates")]
    public async Task<ActionResult> Discard(
        long id,
        [FromServices] DiscardCandidates discard,
        CancellationToken cancellationToken)
    {
        await discard.Execute(id, cancellationToken);

        return NoContent();
    }

    /// <summary>Returns a receipt to Pending and queues it for extraction again.</summary>
    [HttpPost("extraction/rerun")]
    [Produces("application/json")]
    [EndpointName("RerunExtraction")]
    public async Task<ActionResult<ReceiptView>> Rerun(
        long id,
        [FromServices] RequeueExtraction requeue,
        CancellationToken cancellationToken) => Accepted(
            $"/purchases/{id}/extraction",
            await requeue.Execute(id, cancellationToken));
}
