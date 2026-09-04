using Expenses.Application.ReferenceData;
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
        [FromServices] ListUnits units,
        CancellationToken cancellationToken) => Ok(await units.Execute(
            includeInactive ?? false,
            cancellationToken));
}
