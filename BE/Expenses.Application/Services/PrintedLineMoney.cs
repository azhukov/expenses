using Expenses.Application.Dtos;

namespace Expenses.Application.Services;

/// <summary>
/// Brings lines read off a printed receipt to the form the ledger keeps (D7) and the oracle checks
/// (D20), the same form the portal's lines arrive in: every money value in cents, line amounts
/// summing to the total where rounding alone kept them from it, and a list price stated for the
/// whole line.
/// </summary>
public static class PrintedLineMoney
{
    /// <summary>
    /// Each line in cents. A list price is extended by the line's quantity before it is rounded, so
    /// 1.59 a kilogram over 1.358 kg lists at 2.16. A discount printed as an amount is taken as
    /// printed; one printed only as a percentage is that share of the extended list price, so it is
    /// still derived from the list price alone and the discount check still compares two
    /// independently read numbers against the amount. Where only one of the list price and the
    /// discount could be read, neither is kept (D19): a discount is recorded whole or not at all.
    /// </summary>
    public static IReadOnlyList<LineInCents> ToCents(IReadOnlyList<PrintedLine> lines, decimal? total)
    {
        // A line whose amount could not be read leaves nothing to spread a rounding cent across
        // honestly: the lines are rounded each on its own, and the missing amount stays missing.
        if (lines.Any(line => line.Amount is null))
        {
            return [.. lines.Select(line => InCents(line, line.Amount is { } amount ? CentRounding.ToCents(amount) : null))];
        }

        var amounts = CentRounding.ToCentsSummingTo(
            [.. lines.Select(line => line.Amount!.Value)],
            total is { } stated ? CentRounding.ToCents(stated) : null);

        return [.. lines.Select((line, index) => InCents(line, amounts[index]))];
    }

    private static LineInCents InCents(PrintedLine line, decimal? amount)
    {
        decimal? unitPrice = line.UnitPrice is { } unit ? CentRounding.ToCents(unit) : null;

        if (line.ListUnitPrice is not { } listUnit)
        {
            return new LineInCents(amount, unitPrice, null, null);
        }

        decimal exactList = listUnit * (line.Quantity ?? 1m);

        decimal? discount = line switch
        {
            { DiscountAmount: { } printed } => CentRounding.ToCents(printed),
            { DiscountPercent: { } percent } => CentRounding.ToCents(exactList * percent / 100m),
            _ => null,
        };

        return discount is { } taken
            ? new LineInCents(amount, unitPrice, CentRounding.ToCents(exactList), taken)
            : new LineInCents(amount, unitPrice, null, null);
    }
}
