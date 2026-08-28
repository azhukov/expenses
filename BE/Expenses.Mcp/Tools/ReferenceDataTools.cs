using System.ComponentModel;
using Expenses.Application.Merchants;
using Expenses.Application.ReferenceData;
using ModelContextProtocol.Server;

namespace Expenses.Mcp.Tools;

/// <summary>
/// The dictionaries an assistant needs in order to name things by code rather than guessing at
/// display names (D8). Failures are translated once, by the filter in <see cref="ExpensesMcpServer"/>.
/// </summary>
[McpServerToolType]
public sealed class ReferenceDataTools(
    ListCategories listCategories,
    ListUnits listUnits,
    CreateCategory createCategory,
    RenameCategory renameCategory,
    DeactivateCategory deactivateCategory)
{
    [McpServerTool(Name = "list_categories")]
    [Description("""
        Lists the expense categories with their codes. Call this before recording a purchase if you
        are not sure which code to use: categories are referenced by code, and a display name is
        not accepted anywhere.
        """)]
    public async Task<IReadOnlyList<CategoryView>> ListCategories(
        [Description("Include categories that have been retired.")] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        await listCategories.Execute(includeInactive, cancellationToken);

    [McpServerTool(Name = "list_units")]
    [Description("""
        Lists the units a quantity can be expressed in, with their codes, symbols and whether each
        measures mass, volume or a count. Units are fixed; they cannot be created.
        """)]
    public async Task<IReadOnlyList<UnitView>> ListUnits(
        [Description("Include units that have been retired.")] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        await listUnits.Execute(includeInactive, cancellationToken);

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
        CancellationToken cancellationToken = default) =>
        await createCategory.Execute(new CreateCategoryCommand(code, name, parentCode), cancellationToken);

    [McpServerTool(Name = "rename_category")]
    [Description("""
        Changes the display name of a category. Expenses already assigned to it keep their
        assignment, because they reference the code rather than the name.
        """)]
    public async Task<CategoryView> RenameCategory(
        [Description("The code of the category to rename.")] string code,
        [Description("The new display name.")] string name,
        CancellationToken cancellationToken = default) =>
        await renameCategory.Execute(new RenameCategoryCommand(code, name), cancellationToken);

    [McpServerTool(Name = "deactivate_category")]
    [Description("""
        Retires a category so it is no longer offered for new expenses. Existing expenses keep it
        and remain readable. A category with active children cannot be retired until they are.
        """)]
    public async Task<CategoryView> DeactivateCategory(
        [Description("The code of the category to retire.")] string code,
        CancellationToken cancellationToken = default) =>
        await deactivateCategory.Execute(code, cancellationToken);
}

/// <summary>
/// The merchant dictionary, which is learned rather than seeded: an assistant reads it to find out
/// what the ledger already knows about a shop (D18).
/// </summary>
[McpServerToolType]
public sealed class MerchantTools(
    ListMerchants listMerchants,
    SearchMerchants searchMerchants,
    RenameMerchant renameMerchant,
    SetMerchantParent setMerchantParent,
    DeactivateMerchant deactivateMerchant)
{
    [McpServerTool(Name = "list_merchants")]
    [Description("""
        Lists the merchants the ledger has learned from recorded purchases. Merchants are not
        seeded: one appears the first time a purchase names it.
        """)]
    public async Task<IReadOnlyList<MerchantView>> ListMerchants(
        [Description("Include merchants that have been retired.")] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        await listMerchants.Execute(includeInactive, cancellationToken);

    [McpServerTool(Name = "search_merchants")]
    [Description("""
        Searches merchants by name, tolerating accents and small misspellings. It also searches the
        verbatim merchant text kept on purchases, so a shop that never made it into the dictionary
        is still findable by what the receipt said.
        """)]
    public async Task<IReadOnlyList<MerchantMatchView>> SearchMerchants(
        [Description("Part of a merchant name, as the user remembers it.")] string term,
        CancellationToken cancellationToken = default) =>
        await searchMerchants.Execute(term, cancellationToken);

    [McpServerTool(Name = "rename_merchant")]
    [Description("""
        Changes a merchant's display name. Purchases already referencing it continue to do so.
        """)]
    public async Task<MerchantView> RenameMerchant(
        [Description("The merchant identifier.")] long id,
        [Description("The new display name.")] string name,
        CancellationToken cancellationToken = default) =>
        await renameMerchant.Execute(id, name, cancellationToken);

    [McpServerTool(Name = "set_merchant_parent")]
    [Description("""
        Records that one merchant is a branch of another, so reporting can roll a branch's spending
        up to its chain. Pass no parent to detach it.
        """)]
    public async Task<MerchantView> SetMerchantParent(
        [Description("The branch merchant identifier.")] long id,
        [Description("The chain merchant identifier, or null to detach.")] long? parentId = null,
        CancellationToken cancellationToken = default) =>
        await setMerchantParent.Execute(id, parentId, cancellationToken);

    [McpServerTool(Name = "deactivate_merchant")]
    [Description("""
        Retires a merchant so it is no longer offered for new purchases. Existing purchases keep it.
        """)]
    public async Task<MerchantView> DeactivateMerchant(
        [Description("The merchant identifier.")] long id,
        CancellationToken cancellationToken = default) =>
        await deactivateMerchant.Execute(id, cancellationToken);
}
