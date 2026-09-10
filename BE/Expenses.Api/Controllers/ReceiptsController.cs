using Expenses.Application.Dtos;
using Expenses.Application.Errors;
using Expenses.Application.Interfaces;
using Expenses.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Expenses.Api.Controllers;

/// <summary>
/// Receipts and the extraction that reads them. A receipt belongs to exactly one purchase and has
/// no identifier of its own (D11), so every route here hangs off the purchase — except capture,
/// which precedes any purchase and lives on <see cref="CapturesController"/>. As with every adapter
/// action, binding and shaping is all that happens (D1).
/// </summary>
[ApiController]
[Route("purchases/{id:long}")]
public sealed class ReceiptsController : ControllerBase
{
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
        byte[] content = await images.Read(receipt.StorageKey, cancellationToken)
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
        [FromServices] ReceiptService receipts,
        CancellationToken cancellationToken) => Ok(await receipts.Delete(id, cancellationToken));

    /// <summary>Returns a receipt's extraction state, candidates, stage provenance and arithmetic checks.</summary>
    [HttpGet("extraction")]
    [Produces("application/json")]
    [EndpointName("GetExtraction")]
    public async Task<ActionResult<ExtractionResponse>> GetExtraction(
        long id,
        [FromServices] ReceiptService receipts,
        CancellationToken cancellationToken) => Ok(ExtractionResponse.Of(
            await receipts.GetExtractionCandidates(id, cancellationToken)));

    /// <summary>Turns candidate lines — as extracted or as edited — into the expenses of the purchase.</summary>
    [HttpPost("extraction/confirm")]
    [Produces("application/json")]
    [EndpointName("ConfirmCandidates")]
    public async Task<ActionResult<PurchaseView>> Confirm(
        long id,
        [FromBody] ConfirmCandidatesRequest? request,
        [FromServices] ReceiptService receipts,
        CancellationToken cancellationToken)
    {
        RequestGuards.RejectDiscountPercentage(request?.Expenses);

        var edited = request?.Expenses?.Select(expense => expense.ToCommand()).ToList();

        return Ok(await receipts.ConfirmCandidates(id, edited, cancellationToken));
    }

    /// <summary>Removes a receipt's candidate lines, leaving the receipt and the purchase.</summary>
    [HttpPost("extraction/discard")]
    [Produces("application/json")]
    [EndpointName("DiscardCandidates")]
    public async Task<ActionResult> Discard(
        long id,
        [FromServices] ReceiptService receipts,
        CancellationToken cancellationToken)
    {
        await receipts.DiscardCandidates(id, cancellationToken);

        return NoContent();
    }

    /// <summary>Runs extraction again for a purchase's receipt, synchronously, and returns the full result.</summary>
    [HttpPost("extraction/rerun")]
    [Produces("application/json")]
    [EndpointName("RerunExtraction")]
    public async Task<ActionResult<ExtractionResponse>> Rerun(
        long id,
        [FromServices] ReceiptService receipts,
        CancellationToken cancellationToken)
    {
        await receipts.RerunExtraction(id, cancellationToken);

        // Read back rather than returned by the re-run itself, so the shape matches GetExtraction
        // exactly — the candidates it just replaced, in the same response.
        return Ok(ExtractionResponse.Of(await receipts.GetExtractionCandidates(id, cancellationToken)));
    }
}
