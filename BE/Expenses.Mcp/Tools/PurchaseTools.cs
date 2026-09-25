using System.ComponentModel;
using Expenses.Application.Dtos;
using Expenses.Application.Services;
using ModelContextProtocol.Server;

namespace Expenses.Mcp.Tools;

/// <summary>
/// Recording and reading purchases. Every tool here delegates to the same use case the HTTP
/// interface calls, which is what makes the two front doors behave identically (D1). Failures are
/// translated once, by the filter in <see cref="ExpensesMcpServer"/>.
/// </summary>
[McpServerToolType]
public sealed class PurchaseTools(PurchaseService purchases)
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
        CancellationToken cancellationToken = default)
        => RecordPurchaseToolResult.Of(await purchases.Record(
            occurredAt,
            amount,
            [.. expenses.Select(expense => expense.ToCommand())],
            merchant?.ToCommand(),
            cancellationToken: cancellationToken));

    [McpServerTool(Name = "confirm_capture")]
    [Description("""
        Confirms a capture вЂ” made over HTTP, since image bytes are never a tool argument here вЂ” into
        a new purchase. Resubmit exactly what the capture response reported (or corrected values,
        where extraction needed fixing): its temporary key and extraction outcome, together with the
        date, amount and expense lines the user is asserting. The promoted image is attached as the
        purchase's receipt, already in that extraction state; nothing further is extracted. A capture
        made from a fiscal QR payload alone has no temporary key: omit it and resubmit the payload
        instead, and the purchase carries that fiscal invoice and no image. A capture naming neither
        is refused. The expense amounts must sum exactly to the purchase amount. The date may be
        omitted only when the capture's fiscal QR decoded an invoice creation timestamp.
        """)]
    public async Task<RecordPurchaseToolResult> ConfirmCapture(
        [Description("What the capture response reported, to confirm it.")]
        CapturedReceiptArgument capture,
        [Description("The total paid.")] decimal amount,
        [Description("The lines making up the purchase. Their amounts must sum to the total.")]
        IReadOnlyList<ExpenseArgument> expenses,
        [Description("When the purchase happened. Omit only if the capture's fiscal QR decoded a timestamp.")]
        DateTime? occurredAt = null,
        [Description("Where the purchase was made, if known.")] MerchantArgument? merchant = null,
        CancellationToken cancellationToken = default)
        => RecordPurchaseToolResult.Of(await purchases.Record(
            occurredAt,
            amount,
            [.. expenses.Select(expense => expense.ToCommand())],
            merchant?.ToCommand(),
            capture.ToCommand(),
            cancellationToken));

    [McpServerTool(Name = "get_purchase")]
    [Description("""
        Returns one purchase with its expense lines, its merchant and the verbatim merchant text,
        any discounts, and the saving they add up to. Use it when the user asks about a specific
        purchase you already have an identifier for.
        """)]
    public async Task<PurchaseView> GetPurchase(
        [Description("The purchase identifier.")] long id,
        CancellationToken cancellationToken = default)
        => await purchases.Get(id, cancellationToken);

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
        CancellationToken cancellationToken = default)
        => await purchases.List(from, to, skip, take, cancellationToken);
}
