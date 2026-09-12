using System.ComponentModel;
using Expenses.Application.Dtos;
using Expenses.Application.Services;
using ModelContextProtocol.Server;

namespace Expenses.Mcp.Tools;

/// <summary>
/// The dictionaries an assistant needs in order to name things by code rather than guessing at
/// display names (D8). Failures are translated once, by the filter in <see cref="ExpensesMcpServer"/>.
/// </summary>
[McpServerToolType]
public sealed class ReferenceDataTools(CategoryService categories, UnitService units)
{
    [McpServerTool(Name = "list_categories")]
    [Description("""
        Lists the expense categories with their codes. Call this before recording a purchase if you
        are not sure which code to use: categories are referenced by code, and a display name is
        not accepted anywhere.
        """)]
    public async Task<IReadOnlyList<CategoryView>> ListCategories(
        [Description("Include categories that have been retired.")] bool includeInactive = false,
        CancellationToken cancellationToken = default)
        => await categories.List(includeInactive, cancellationToken);

    [McpServerTool(Name = "list_units")]
    [Description("""
        Lists the units a quantity can be expressed in, with their codes, symbols and whether each
        measures mass, volume or a count. Units are fixed; they cannot be created.
        """)]
    public async Task<IReadOnlyList<UnitView>> ListUnits(
        [Description("Include units that have been retired.")] bool includeInactive = false,
        CancellationToken cancellationToken = default)
        => await units.List(includeInactive, cancellationToken);

    [McpServerTool(Name = "create_category")]
    [Description("""
        Creates a new expense category. Use it when the user wants to track something none of the
        existing categories covers. The code is permanent and cannot be changed afterwards; the
        display name can.
        """)]
    public async Task<CategoryView> CreateCategory(
        [Description("A new, permanent code such as HOBBY_SUPPLIES.")] string code,
        [Description("The display name.")] string name,
        [Description("The code of a parent category, to nest this one beneath it.")] string? parentCode = null,
        CancellationToken cancellationToken = default)
        => await categories.Create(code, name, parentCode, cancellationToken);

    [McpServerTool(Name = "rename_category")]
    [Description("""
        Changes the display name of a category. Expenses already assigned to it keep their
        assignment, because they reference the code rather than the name.
        """)]
    public async Task<CategoryView> RenameCategory(
        [Description("The code of the category to rename.")] string code,
        [Description("The new display name.")] string name,
        CancellationToken cancellationToken = default)
        => await categories.Rename(code, name, cancellationToken: cancellationToken);

    [McpServerTool(Name = "deactivate_category")]
    [Description("""
        Retires a category so it is no longer offered for new expenses. Existing expenses keep it
        and remain readable. A category with active children cannot be retired until they are.
        """)]
    public async Task<CategoryView> DeactivateCategory(
        [Description("The code of the category to retire.")] string code,
        CancellationToken cancellationToken = default)
        => await categories.Deactivate(code, cancellationToken);
}
