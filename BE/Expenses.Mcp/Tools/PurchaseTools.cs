using System.ComponentModel;
using Expenses.Application.Purchases;
using ModelContextProtocol.Server;

namespace Expenses.Mcp.Tools;

/// <summary>
/// A line as an assistant supplies it. Reference data is named by <c>code</c>, never by display
/// name: <c>GROCERIES</c> is unambiguous where "Groceries" is not (D8).
/// </summary>
public sealed record ExpenseArgument(
    [property: Description("What was bought, as it should read in the ledger.")]
    string Description,
    [property: Description("What was actually paid for this line. Authoritative.")]
    decimal Amount,
    [property: Description("How much was bought. Defaults to 1.")]
    decimal? Quantity = null,
    [property: Description("Unit code, such as KG. Never a display name.")]
    string? UnitCode = null,
    [property: Description("Price per unit. Descriptive: it need not multiply out to the amount.")]
    decimal? UnitPrice = null,
    [property: Description("Category code, such as GROCERIES. Never a display name.")]
    string? CategoryCode = null,
    [property: Description("What the item normally costs, when a discount was printed.")]
    decimal? ListUnitPrice = null,
    [property: Description("How much was taken off, as a positive amount. Never a percentage.")]
    decimal? DiscountAmount = null)
{
    public ExpenseCommand ToCommand() => new(
        Description,
        Amount,
        Quantity,
        UnitCode,
        UnitPrice,
        CategoryCode,
        ListUnitPrice: ListUnitPrice,
        DiscountAmount: DiscountAmount);
}

public sealed record MerchantArgument(
    [property: Description("The merchant as printed on the receipt or named by the user.")]
    string Text,
    [property: Description("The merchant's tax identification number, where the receipt printed one.")]
    string? TaxId = null)
{
    public MerchantCommand ToCommand() => new(Text, TaxId);
}

/// <summary>
/// What recording produced. <see cref="Summary"/> is the sentence an assistant relays: a repeated
/// call is a success that says so, not an error it would try to route around (D3).
/// </summary>
public sealed record RecordPurchaseToolResult(
    string Summary,
    bool AlreadyRecorded,
    bool MerchantNewlyAdded,
    PurchaseView Purchase)
{
    public static RecordPurchaseToolResult Of(RecordPurchaseResult result)
    {
        var merchant = result.Merchant is null
            ? string.Empty
            : $" at {result.Merchant.Name}"
              + (result.MerchantNewlyAdded ? ", newly added to the merchant list" : string.Empty);

        var summary = result.AlreadyRecorded
            ? $"This purchase was already recorded: {result.Purchase.Amount} on "
              + $"{result.Purchase.OccurredAt:yyyy-MM-dd HH:mm}, identifier {result.Purchase.Id}. "
              + "Nothing was changed."
            : $"Recorded {result.Purchase.Amount} on {result.Purchase.OccurredAt:yyyy-MM-dd HH:mm}"
              + $"{merchant}, identifier {result.Purchase.Id}.";

        return new RecordPurchaseToolResult(
            summary,
            result.AlreadyRecorded,
            result.MerchantNewlyAdded,
            result.Purchase);
    }
}

/// <summary>
/// Recording and reading purchases. Every tool here delegates to the same use case the HTTP
/// interface calls, which is what makes the two front doors behave identically (D1). Failures are
/// translated once, by the filter in <see cref="ExpensesMcpServer"/>.
/// </summary>
[McpServerToolType]
public sealed class PurchaseTools(RecordPurchase recordPurchase, GetPurchase getPurchase, ListPurchases listPurchases)
{
    [McpServerTool(Name = "record_purchase")]
    [Description("""
        Records a purchase and its expense lines in the ledger. Use it whenever the user says they
        bought or paid for something. Safe to call again with the same arguments: a purchase whose
        date, time and amount match one already recorded is returned as it stands rather than
        duplicated, and the result says so. The expense amounts must sum exactly to the purchase
        amount. Reference data is named by code, never by display name.
        """)]
    public async Task<RecordPurchaseToolResult> RecordPurchase(
        [Description("When the purchase happened, as local wall-clock time. No timezone is applied.")]
        DateTime occurredAt,
        [Description("The total paid.")] decimal amount,
        [Description("The lines making up the purchase. Their amounts must sum to the total.")]
        IReadOnlyList<ExpenseArgument> expenses,
        [Description("Where the purchase was made, if known.")] MerchantArgument? merchant = null,
        CancellationToken cancellationToken = default) =>
        RecordPurchaseToolResult.Of(await recordPurchase.Execute(
            new RecordPurchaseCommand(
                occurredAt,
                amount,
                [.. expenses.Select(expense => expense.ToCommand())],
                merchant?.ToCommand()),
            cancellationToken));

    [McpServerTool(Name = "get_purchase")]
    [Description("""
        Returns one purchase with its expense lines, its merchant and the verbatim merchant text,
        any discounts, and the saving they add up to. Use it when the user asks about a specific
        purchase you already have an identifier for.
        """)]
    public async Task<PurchaseView> GetPurchase(
        [Description("The purchase identifier.")] long id,
        CancellationToken cancellationToken = default) =>
        await getPurchase.Execute(id, cancellationToken);

    [McpServerTool(Name = "list_purchases")]
    [Description("""
        Lists purchases in a date range, most recent first. Use it to answer questions about
        spending over a period. Both dates are inclusive; omit them to read the most recent
        purchases.
        """)]
    public async Task<IReadOnlyList<PurchaseView>> ListPurchases(
        [Description("Earliest occurrence date to include, inclusive.")] DateOnly? from = null,
        [Description("Latest occurrence date to include, inclusive.")] DateOnly? to = null,
        [Description("How many purchases to skip, for paging.")] int skip = 0,
        [Description("How many purchases to return. At most 200.")] int? take = null,
        CancellationToken cancellationToken = default) =>
        await listPurchases.Execute(new ListPurchasesQuery(from, to, skip, take), cancellationToken);
}
