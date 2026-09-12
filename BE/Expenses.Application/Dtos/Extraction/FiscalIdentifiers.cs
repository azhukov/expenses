namespace Expenses.Application.Dtos;

/// <summary>
/// What a fiscal code carries, exactly as read, with no format imposed (D10). Held per source so a
/// disagreement can be reported rather than one value silently preferred.
///
/// The identifiers are only part of it: a decoded code also states the issuer, when the invoice was
/// created and what it came to, and those three are what the verification portal is asked for. They
/// travel here rather than in a parallel channel so that a later stage receives everything an
/// earlier one decoded without the cascade's seam changing shape (D22, D23).
/// </summary>
public sealed record FiscalIdentifiers(
    string? Ikof = null,
    string? Jikr = null,
    string? IssuerTaxNumber = null,
    string? CreatedAt = null,
    decimal? Total = null)
{
    public static readonly FiscalIdentifiers None = new();

    public bool IsEmpty
        => string.IsNullOrWhiteSpace(Ikof)
        && string.IsNullOrWhiteSpace(Jikr)
        && string.IsNullOrWhiteSpace(IssuerTaxNumber)
        && string.IsNullOrWhiteSpace(CreatedAt);
}
