using Expenses.Application.Merchants;
using Microsoft.AspNetCore.Mvc;

namespace Expenses.Api.Controllers;

/// <summary>The merchant dictionary the ledger learns as purchases are recorded.</summary>
[ApiController]
[Produces("application/json")]
[Route("merchants")]
public sealed class MerchantsController : ControllerBase
{
    /// <summary>Lists the learned merchant dictionary.</summary>
    [HttpGet]
    [EndpointName("ListMerchants")]
    public async Task<ActionResult<IReadOnlyList<MerchantView>>> List(
        [FromQuery] bool? includeInactive,
        [FromServices] ListMerchants merchants,
        CancellationToken cancellationToken) => Ok(await merchants.Execute(
            includeInactive ?? false,
            cancellationToken));

    /// <summary>Searches merchant names and the verbatim merchant text on purchases, tolerating accents.</summary>
    [HttpGet("search")]
    [EndpointName("SearchMerchants")]
    public async Task<ActionResult<IReadOnlyList<MerchantMatchView>>> Search(
        [FromQuery] string term,
        [FromServices] SearchMerchants search,
        CancellationToken cancellationToken) => Ok(await search.Execute(term, cancellationToken));

    /// <summary>Renames a merchant; purchases continue to reference it.</summary>
    [HttpPut("{id:long}")]
    [EndpointName("RenameMerchant")]
    public async Task<ActionResult<MerchantView>> Rename(
        long id,
        RenameMerchantRequest request,
        [FromServices] RenameMerchant rename,
        CancellationToken cancellationToken) => Ok(await rename.Execute(id, request.Name, cancellationToken));

    /// <summary>Records a branch beneath the chain it belongs to.</summary>
    [HttpPut("{id:long}/parent")]
    [EndpointName("SetMerchantParent")]
    public async Task<ActionResult<MerchantView>> SetParent(
        long id,
        SetMerchantParentRequest request,
        [FromServices] SetMerchantParent setParent,
        CancellationToken cancellationToken) => Ok(await setParent.Execute(id, request.ParentId, cancellationToken));

    /// <summary>Retires a merchant, leaving purchases that reference it unchanged.</summary>
    [HttpPost("{id:long}/deactivate")]
    [EndpointName("DeactivateMerchant")]
    public async Task<ActionResult<MerchantView>> Deactivate(
        long id,
        [FromServices] DeactivateMerchant deactivate,
        CancellationToken cancellationToken) => Ok(await deactivate.Execute(id, cancellationToken));
}
