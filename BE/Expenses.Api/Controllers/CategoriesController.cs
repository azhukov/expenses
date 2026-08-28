using Expenses.Application.ReferenceData;
using Microsoft.AspNetCore.Mvc;

namespace Expenses.Api.Controllers;

/// <summary>The category dictionary, seeded and user-extended (D8).</summary>
[ApiController]
[Produces("application/json")]
[Route("categories")]
public sealed class CategoriesController : ControllerBase
{
    /// <summary>Lists categories, active only unless inactive ones are requested.</summary>
    [HttpGet]
    [EndpointName("ListCategories")]
    public async Task<ActionResult<IReadOnlyList<CategoryView>>> List(
        [FromQuery] bool? includeInactive,
        [FromServices] ListCategories categories,
        CancellationToken cancellationToken) => Ok(await categories.Execute(
            includeInactive ?? false,
            cancellationToken));

    /// <summary>Creates a user category with a new code.</summary>
    [HttpPost]
    [EndpointName("CreateCategory")]
    public async Task<ActionResult<CategoryView>> Create(
        CreateCategoryRequest request,
        [FromServices] CreateCategory create,
        CancellationToken cancellationToken)
    {
        var created = await create.Execute(
            new CreateCategoryCommand(request.Code, request.Name, request.ParentCode),
            cancellationToken);

        return Created($"/categories/{created.Code}", created);
    }

    /// <summary>Renames a category; a request that would change its code is rejected.</summary>
    [HttpPut("{code}")]
    [EndpointName("RenameCategory")]
    public async Task<ActionResult<CategoryView>> Rename(
        string code,
        RenameCategoryRequest request,
        [FromServices] RenameCategory rename,
        CancellationToken cancellationToken) => Ok(await rename.Execute(
            new RenameCategoryCommand(code, request.Name, request.Code),
            cancellationToken));

    /// <summary>Retires a category, leaving expenses that reference it unchanged.</summary>
    [HttpPost("{code}/deactivate")]
    [EndpointName("DeactivateCategory")]
    public async Task<ActionResult<CategoryView>> Deactivate(
        string code,
        [FromServices] DeactivateCategory deactivate,
        CancellationToken cancellationToken) => Ok(await deactivate.Execute(code, cancellationToken));

    /// <summary>Deletes a user-created category; a seeded one is refused.</summary>
    [HttpDelete("{code}")]
    [EndpointName("DeleteCategory")]
    public async Task<ActionResult> Delete(
        string code,
        [FromServices] DeleteCategory delete,
        CancellationToken cancellationToken)
    {
        await delete.Execute(code, cancellationToken);

        return NoContent();
    }
}
