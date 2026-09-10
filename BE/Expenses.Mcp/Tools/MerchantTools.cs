using System.ComponentModel;
using Expenses.Application.Dtos;
using Expenses.Application.Services;
using ModelContextProtocol.Server;

namespace Expenses.Mcp.Tools;

/// <summary>
/// The merchant dictionary, which is learned rather than seeded: an assistant reads it to find out
/// what the ledger already knows about a shop (D18).
/// </summary>
[McpServerToolType]
public sealed class MerchantTools(MerchantService merchants)
{
    [McpServerTool(Name = "list_merchants")]
    [Description("""
        Lists the merchants the ledger has learned from recorded purchases. Merchants are not
        seeded: one appears the first time a purchase names it.
        """)]
    public async Task<IReadOnlyList<MerchantView>> ListMerchants(
        [Description("Include merchants that have been retired.")] bool includeInactive = false,
        CancellationToken cancellationToken = default)
        => await merchants.List(includeInactive, cancellationToken);

    [McpServerTool(Name = "search_merchants")]
    [Description("""
        Searches merchants by name, tolerating accents and small misspellings. It also searches the
        verbatim merchant text kept on purchases, so a shop that never made it into the dictionary
        is still findable by what the receipt said.
        """)]
    public async Task<IReadOnlyList<MerchantMatchView>> SearchMerchants(
        [Description("Part of a merchant name, as the user remembers it.")] string term,
        CancellationToken cancellationToken = default)
        => await merchants.Search(term, cancellationToken);

    [McpServerTool(Name = "rename_merchant")]
    [Description("""
        Changes a merchant's display name. Purchases already referencing it continue to do so.
        """)]
    public async Task<MerchantView> RenameMerchant(
        [Description("The merchant identifier.")] long id,
        [Description("The new display name.")] string name,
        CancellationToken cancellationToken = default)
        => await merchants.Rename(id, name, cancellationToken);

    [McpServerTool(Name = "set_merchant_parent")]
    [Description("""
        Records that one merchant is a branch of another, so reporting can roll a branch's spending
        up to its chain. Pass no parent to detach it.
        """)]
    public async Task<MerchantView> SetMerchantParent(
        [Description("The branch merchant identifier.")] long id,
        [Description("The chain merchant identifier, or null to detach.")] long? parentId = null,
        CancellationToken cancellationToken = default)
        => await merchants.SetParent(id, parentId, cancellationToken);

    [McpServerTool(Name = "deactivate_merchant")]
    [Description("""
        Retires a merchant so it is no longer offered for new purchases. Existing purchases keep it.
        """)]
    public async Task<MerchantView> DeactivateMerchant(
        [Description("The merchant identifier.")] long id,
        CancellationToken cancellationToken = default)
        => await merchants.Deactivate(id, cancellationToken);
}
