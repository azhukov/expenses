namespace Expenses.Application.Dtos;

/// <summary>
/// One row of a ledger export: an expense line, carrying the identifier, occurrence and merchant of
/// the purchase it belongs to. The fields are the export's columns, in order. References are names,
/// never identifiers, falling back to the verbatim text where nothing was matched (D1 of
/// add-csv-export). The purchase total is deliberately absent, because repeating it on every line
/// is what makes a spreadsheet sum overcount.
/// </summary>
public sealed record ExportLine(
    long PurchaseId,
    DateTime OccurredAt,
    string? Merchant,
    string Description,
    decimal Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? ListUnitPrice,
    decimal? DiscountAmount,
    string? Category,
    decimal Amount);
