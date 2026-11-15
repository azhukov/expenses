namespace Expenses.Application.Dtos;

/// <summary>
/// One printed line's money in cents (D7), in the shape a candidate carries it. <paramref name="ListPrice"/>
/// is the whole line's price before its discount — the unit list price extended by the quantity —
/// because the discount check it feeds is a line-level one, as the portal's mapping states it too.
/// The list price and the discount are both present or both absent (D19).
/// </summary>
public sealed record LineInCents(
    decimal Amount,
    decimal? UnitPrice,
    decimal? ListPrice,
    decimal? Discount);
