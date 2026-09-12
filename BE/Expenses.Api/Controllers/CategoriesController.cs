using Expenses.Application.Dtos;
using Expenses.Application.Services;
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
        [FromServices] CategoryService categories,
        CancellationToken cancellationToken) => Ok(await categories.List(
            includeInactive ?? false,
            cancellationToken));

    /// <summary>Creates a user category with a new code.</summary>
    [HttpPost]
    [EndpointName("CreateCategory")]
    public async Task<ActionResult<CategoryView>> Create(
        CreateCategoryRequest request,
        [FromServices] CategoryService categories,
        CancellationToken cancellationToken)
    {
        var created = await categories.Create(
            request.Code,
            request.Name,
            request.ParentCode,
            cancellationToken);

        return Created($"/categories/{created.Code}", created);
    }

    /// <summary>Renames a category; a request that would change its code is rejected.</summary>
    [HttpPut("{code}")]
    [EndpointName("RenameCategory")]
    public async Task<ActionResult<CategoryView>> Rename(
        string code,
        RenameCategoryRequest request,
        [FromServices] CategoryService categories,
        CancellationToken cancellationToken) => Ok(await categories.Rename(
            code,
            request.Name,
            request.Code,
            cancellationToken));

    /// <summary>Retires a category, leaving expenses that reference it unchanged.</summary>
    [HttpPost("{code}/deactivate")]
    [EndpointName("DeactivateCategory")]
    public async Task<ActionResult<CategoryView>> Deactivate(
        string code,
        [FromServices] CategoryService categories,
        CancellationToken cancellationToken) => Ok(await categories.Deactivate(code, cancellationToken));

    /// <summary>Deletes a user-created category; a seeded one is refused.</summary>
    [HttpDelete("{code}")]
    [EndpointName("DeleteCategory")]
    public async Task<ActionResult> Delete(
        string code,
        [FromServices] CategoryService categories,
        CancellationToken cancellationToken)
    {
        await categories.Delete(code, cancellationToken);

        return NoContent();
    }
}
