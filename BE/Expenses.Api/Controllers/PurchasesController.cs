using Expenses.Application.Purchases;
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
        [FromServices] RecordPurchase recordPurchase,
        CancellationToken cancellationToken)
    {
        RequestGuards.RejectDiscountPercentage(request.Expenses);

        var result = await recordPurchase.Execute(
            new RecordPurchaseCommand(
                request.OccurredAt,
                request.Amount,
                [.. request.Expenses.Select(expense => expense.ToCommand())],
                request.Merchant?.ToCommand(),
                request.Capture?.ToCommand()),
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
        [FromServices] GetPurchase getPurchase,
        CancellationToken cancellationToken) => Ok(await getPurchase.Execute(id, cancellationToken));

    /// <summary>Lists purchases in an occurrence date range, most recent first.</summary>
    [HttpGet]
    [EndpointName("ListPurchases")]
    public async Task<ActionResult<IReadOnlyList<PurchaseView>>> List(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        [FromServices] ListPurchases listPurchases,
        CancellationToken cancellationToken) => Ok(await listPurchases.Execute(
            new ListPurchasesQuery(from, to, skip ?? 0, take),
            cancellationToken));
}
