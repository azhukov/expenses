using Expenses.Application.Dtos;
using Expenses.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Expenses.Api.Controllers;

/// <summary>
/// The HTTP front door for purchases. Every action binds a request, calls one use case, and shapes
/// the result: no rule lives here, because the same rule would then have to live in the MCP
/// adapter too (D1).
/// </summary>
[ApiController]
[Produces("application/json")]
[Route("purchases")]
public sealed class PurchasesController : ControllerBase
{
    /// <summary>Records a purchase and its expense lines, absorbing a repeated submission.</summary>
    [HttpPost]
    [EndpointName("RecordPurchase")]
    public async Task<ActionResult<RecordPurchaseResponse>> Record(
        RecordPurchaseRequest request,
        [FromServices] PurchaseService purchases,
        CancellationToken cancellationToken)
    {
        RequestGuards.RejectDiscountPercentage(request.Expenses);

        var result = await purchases.Record(
            request.OccurredAt,
            request.Amount,
            [.. request.Expenses.Select(expense => expense.ToCommand())],
            request.Merchant?.ToCommand(),
            request.Capture?.ToCommand(),
            cancellationToken);

        var response = RecordPurchaseResponse.Of(result);

        // 201 for a new purchase, 200 for one that was already recorded — the same success,
        // told apart without an error an assistant would try to route around (D3).
        return result.AlreadyRecorded
            ? Ok(response)
            : Created($"/purchases/{response.Id}", response);
    }

    /// <summary>Returns one purchase with its expenses, merchant and derived saving.</summary>
    [HttpGet("{id:long}")]
    [EndpointName("GetPurchase")]
    public async Task<ActionResult<PurchaseView>> Get(
        long id,
        [FromServices] PurchaseService purchases,
        CancellationToken cancellationToken) => Ok(await purchases.Get(id, cancellationToken));

    /// <summary>Lists purchases in an occurrence date range, most recent first.</summary>
    [HttpGet]
    [EndpointName("ListPurchases")]
    public async Task<ActionResult<IReadOnlyList<PurchaseView>>> List(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        [FromServices] PurchaseService purchases,
        CancellationToken cancellationToken) => Ok(await purchases.List(from, to, skip ?? 0, take, cancellationToken));

    /// <summary>How many purchases the ledger holds, across every date.</summary>
    [HttpGet("count")]
    [EndpointName("CountPurchases")]
    public async Task<ActionResult<PurchaseCountResponse>> Count(
        [FromServices] PurchaseService purchases,
        CancellationToken cancellationToken) => Ok(new PurchaseCountResponse(await purchases.Count(cancellationToken)));

    /// <summary>
    /// Deletes every purchase, provided the caller states how many it expects to delete and the
    /// ledger still holds exactly that many. HTTP-only on purpose: no MCP tool clears the ledger
    /// (D5 of add-clear-all-expenses). The expected count is a query parameter because DELETE bodies
    /// are dropped by too many intermediaries, and it is bound as nullable so that a missing one is
    /// refused by the use case, in its own words, rather than by model binding.
    /// </summary>
    [HttpDelete]
    [EndpointName("ClearPurchases")]
    public async Task<ActionResult<ClearLedgerResponse>> Clear(
        [FromQuery] int? expectedCount,
        [FromServices] PurchaseService purchases,
        [FromServices] ILogger<PurchasesController> logger,
        CancellationToken cancellationToken)
    {
        var result = await purchases.ClearAll(expectedCount, cancellationToken);

        foreach (string storageKey in result.ReceiptFilesNotRemoved)
        {
            // Unreferenced and safe to discard; logged because the Application ring cannot log.
            logger.LogWarning(
                "Cleared the ledger but could not delete the receipt file {StorageKey}; it is unreferenced and safe to remove.",
                storageKey);
        }

        return Ok(new ClearLedgerResponse(result.Deleted));
    }
}
