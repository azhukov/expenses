namespace Expenses.Desktop.Core.Ledger;

/// <summary>
/// The fiscal identifiers known from one source. <see cref="CreatedAt"/> is what lets a purchase be
/// recorded without the user entering a date.
/// </summary>
public sealed record FiscalIdentifiers(
    string? Ikof,
    string? Jikr,
    string? IssuerTaxNumber,
    DateTime? CreatedAt,
    decimal? Total);
