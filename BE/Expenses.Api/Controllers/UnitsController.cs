using Expenses.Application.Dtos;
using Expenses.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Expenses.Api.Controllers;

/// <summary>The unit dictionary a quantity is measured in.</summary>
[ApiController]
[Produces("application/json")]
[Route("units")]
public sealed class UnitsController : ControllerBase
{
    /// <summary>Lists the unit dictionary.</summary>
    [HttpGet]
    [EndpointName("ListUnits")]
    public async Task<ActionResult<IReadOnlyList<UnitView>>> List(
        [FromQuery] bool? includeInactive,
        [FromServices] UnitService units,
        CancellationToken cancellationToken) => Ok(await units.List(
            includeInactive ?? false,
            cancellationToken));

    /// <summary>
    /// Adds a spelling a receipt may print for the unit. Adding one the unit already has succeeds
    /// with the same response, so repeating the request is safe; units themselves are never created.
    /// </summary>
    [HttpPost("{code}/aliases")]
    [EndpointName("AddUnitAlias")]
    public async Task<ActionResult<UnitView>> AddAlias(
        string code,
        [FromBody] AddUnitAliasRequest request,
        [FromServices] UnitService units,
        CancellationToken cancellationToken) => Ok(await units.AddAlias(
            code,
            request.Alias,
            cancellationToken));

    /// <summary>
    /// Removes a spelling from the unit. The alias travels as a query parameter rather than a path
    /// segment, because a normalised alias may keep an inner '/' or '.' that routing does not
    /// round-trip (D8).
    /// </summary>
    [HttpDelete("{code}/aliases")]
    [EndpointName("RemoveUnitAlias")]
    public async Task<IActionResult> RemoveAlias(
        string code,
        [FromQuery] string alias,
        [FromServices] UnitService units,
        CancellationToken cancellationToken)
    {
        await units.RemoveAlias(code, alias, cancellationToken);

        return NoContent();
    }
}
