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
}
