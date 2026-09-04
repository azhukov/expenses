using Microsoft.AspNetCore.Mvc;

namespace Expenses.Api.Controllers;

/// <summary>
/// Exists so that the generic-failure path is reachable by a test: every other route fails through
/// an ExpensesException, and a handler that has never seen an unexpected exception is untested.
/// </summary>
[ApiController]
[Route("diagnostics")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class DiagnosticsController : ControllerBase
{
    [HttpGet("failure")]
    public ActionResult Failure()
        => throw new InvalidOperationException("A deliberate failure, for the generic error path.");
}
