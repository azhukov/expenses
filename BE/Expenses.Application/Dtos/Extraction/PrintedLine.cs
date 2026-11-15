namespace Expenses.Application.Dtos;

/// <summary>
/// One line's money as a reader took it off a printed receipt, before it is brought to the ledger's
/// precision. Tills print some of it to four places (IDEA's 1.536 kg × 1.49 = 2.2886), state a list
/// price per unit rather than per line, and print a discount as a percentage (Aroma's
/// "Cijena bez popusta: 8,50 Popust: 47,19%") as often as an amount.
/// </summary>
public sealed record PrintedLine(
    decimal? Amount,
    decimal? Quantity = null,
    decimal? UnitPrice = null,
    decimal? ListUnitPrice = null,
    decimal? DiscountAmount = null,
    decimal? DiscountPercent = null);
