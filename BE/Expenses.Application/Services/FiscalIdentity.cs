using System.Globalization;
using Expenses.Application.Dtos;

namespace Expenses.Application.Services;

/// <summary>
/// Reads the identifiers out of what a fiscal QR carries. The payload is a verification URL whose
/// parameters name them; anything else is kept as it stands, because no format is
/// imposed on a fiscal identifier anywhere in this design (D10).
///
/// One implementation, reachable from every layer that handles a payload, because a payload now
/// arrives by two routes — supplied by a client at capture, or decoded from the stored image — and
/// two parsers would drift about a format with two traps in it: the parameters live in the URL
/// fragment rather than the query, and the creation timestamp carries a '+' that any form decoder
/// turns into a space (D30).
///
/// **The payload is parsed and never dereferenced.** It looks exactly like something to fetch; it
/// is not. The verification service is addressed from configuration, and only this payload's
/// parameters are read. Nothing here performs I/O of any kind.
/// </summary>
public static class FiscalIdentity
{
    public static FiscalIdentifiers From(string payload)
    {
        if (!Uri.TryCreate(payload, UriKind.Absolute, out var url))
        {
            return new FiscalIdentifiers(payload.Trim());
        }

        // Montenegro's portal is fragment-routed, so its parameters live after the '#' rather than
        // in the query. Both are searched, because nothing else guarantees which one an ERP prints.
        var query = Parameters(url.Query);
        var fragment = Parameters(
            url.Fragment.Contains('?', StringComparison.Ordinal)
                ? url.Fragment[(url.Fragment.IndexOf('?', StringComparison.Ordinal) + 1)..]
                : string.Empty);

        string? ikof = Parameter(query, fragment, "iic", "ikof");
        string? issuerTaxNumber = Parameter(query, fragment, "tin");
        string? createdAt = Parameter(query, fragment, "crtd");

        // No JIKR. An earlier version of this read `crtd` вЂ” the creation timestamp вЂ” into the JIKR
        // slot, so every JIKR the shipped code recorded was a timestamp. The JIKR appears nowhere in
        // the code at all; it is simply not yet known until the portal answers with it (D24).
        string? jikr = Parameter(query, fragment, "jikr");

        return ikof is null && jikr is null && issuerTaxNumber is null && createdAt is null
            ? new FiscalIdentifiers(payload.Trim())
            : new FiscalIdentifiers(
                ikof,
                jikr,
                issuerTaxNumber,
                createdAt,
                Total(Parameter(query, fragment, "prc")));
    }

    /// <summary>
    /// The invoice total the code states. Read at the invariant culture, because the portal prints
    /// a decimal point wherever the receipt was issued and wherever this happens to run.
    /// </summary>
    private static decimal? Total(string? value)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal total)
            ? total
            : null;

    private static string? Parameter(
        IReadOnlyDictionary<string, string> query,
        IReadOnlyDictionary<string, string> fragment,
        params string[] names)
        => names
            .SelectMany(name => new[]
            {
                query.GetValueOrDefault(name),
                fragment.GetValueOrDefault(name),
            })
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    /// <summary>
    /// Split by hand rather than with a form decoder, because a form decoder reads '+' as a space
    /// and the creation timestamp is printed with a '+' in its UTC offset вЂ” `2026-08-29T14:59:22+02:00`
    /// would arrive as a space and the portal would be asked about an invoice created at no time at
    /// all. Percent-escapes are still decoded; '+' is left as the character it is.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Parameters(string text)
        => text.TrimStart('?', '#')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .GroupBy(pair => Uri.UnescapeDataString(pair[0]), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => Uri.UnescapeDataString(group.First()[1]),
                StringComparer.OrdinalIgnoreCase);
}
