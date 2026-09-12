using Expenses.Application.Dtos;
using Expenses.Application.Services;
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
        [FromServices] MerchantService merchants,
        CancellationToken cancellationToken) => Ok(await merchants.List(
            includeInactive ?? false,
            cancellationToken));

    /// <summary>Searches merchant names and the verbatim merchant text on purchases, tolerating accents.</summary>
    [HttpGet("search")]
    [EndpointName("SearchMerchants")]
    public async Task<ActionResult<IReadOnlyList<MerchantMatchView>>> Search(
        [FromQuery] string term,
        [FromServices] MerchantService merchants,
        CancellationToken cancellationToken) => Ok(await merchants.Search(term, cancellationToken));

    /// <summary>Renames a merchant; purchases continue to reference it.</summary>
    [HttpPut("{id:long}")]
    [EndpointName("RenameMerchant")]
    public async Task<ActionResult<MerchantView>> Rename(
        long id,
        RenameMerchantRequest request,
        [FromServices] MerchantService merchants,
        CancellationToken cancellationToken) => Ok(await merchants.Rename(id, request.Name, cancellationToken));

    /// <summary>Records a branch beneath the chain it belongs to.</summary>
    [HttpPut("{id:long}/parent")]
    [EndpointName("SetMerchantParent")]
    public async Task<ActionResult<MerchantView>> SetParent(
        long id,
        SetMerchantParentRequest request,
        [FromServices] MerchantService merchants,
        CancellationToken cancellationToken) => Ok(await merchants.SetParent(id, request.ParentId, cancellationToken));

    /// <summary>Retires a merchant, leaving purchases that reference it unchanged.</summary>
    [HttpPost("{id:long}/deactivate")]
    [EndpointName("DeactivateMerchant")]
    public async Task<ActionResult<MerchantView>> Deactivate(
        long id,
        [FromServices] MerchantService merchants,
        CancellationToken cancellationToken) => Ok(await merchants.Deactivate(id, cancellationToken));
}
