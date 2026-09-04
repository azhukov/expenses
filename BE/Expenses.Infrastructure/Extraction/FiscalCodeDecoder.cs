using System.Globalization;
using Expenses.Application.Abstractions;
using Expenses.Application.Extraction;
using Expenses.Infrastructure.Receipts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXingCpp;
using Image = SixLabors.ImageSharp.Image;

namespace Expenses.Infrastructure.Extraction;

/// <summary>
/// Decodes a fiscal QR from stored image bytes with zxing-cpp (D21).
///
/// Measured on three photographed thermal receipts, at four rotations and five scales, on the full
/// photograph and on a hand-cropped perfectly framed symbol: ZXing.Net read none of them, while
/// zxing-cpp reads the Megapromet symbol straight off the unmodified 4000x3000 photograph and
/// still reads neither Aroma one. So the wall D20 recorded is real but per-till, not universal —
/// and the preprocessing ladder that used to be here is gone, because the measured hit needed none
/// of it and no amount of it rescued a measured miss.
///
/// A miss is therefore an ordinary, expected outcome on some tills, and nothing may depend on a hit.
/// </summary>
internal sealed class FiscalCodeDecoder : IFiscalCodeDecoder
{
    public Task<FiscalIdentifiers?> Decode(
        ReceiptImageContent image,
        CancellationToken cancellationToken = default)
    {
        // Neither format is an image ImageSharp reads; both are simply a miss, which the caller
        // already treats as ordinary.
        if (image.ContentType is ReceiptContent.Pdf or ReceiptContent.Heic)
        {
            return Task.FromResult<FiscalIdentifiers?>(null);
        }

        // zxing-cpp reads luminance. Decoding straight to L8 is both what it wants and less
        // memory than a colour frame of a 12-megapixel photograph.
        using var luminance = Image.Load<L8>(image.Content);
        var pixels = new byte[luminance.Width * luminance.Height];
        luminance.CopyPixelDataTo(pixels);

        var reader = new BarcodeReader
        {
            Formats = BarcodeFormat.QRCode,
            TryHarder = true,
            TryRotate = true,
            TryInvert = true,
            TryDownscale = true,
        };

        var decoded = reader
            .From(new ImageView(pixels, luminance.Width, luminance.Height, ImageFormat.Lum, 0, 0))
            .FirstOrDefault(barcode => barcode.IsValid && barcode.Text.Length > 0);

        return Task.FromResult(decoded is null ? null : FiscalIdentity.From(decoded.Text));
    }
}

/// <summary>
/// Reads the identifiers out of what a fiscal QR carries. The payload is a verification URL whose
/// parameters name them; anything else is kept as it stands, because no format is
/// imposed on a fiscal identifier anywhere in this design (D10).
/// </summary>
internal static class FiscalIdentity
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

        var ikof = Parameter(query, fragment, "iic", "ikof");
        var issuerTaxNumber = Parameter(query, fragment, "tin");
        var createdAt = Parameter(query, fragment, "crtd");

        // No JIKR. An earlier version of this read `crtd` — the creation timestamp — into the JIKR
        // slot, so every JIKR the shipped code recorded was a timestamp. The JIKR appears nowhere in
        // the code at all; it is simply not yet known until the portal answers with it (D24).
        var jikr = Parameter(query, fragment, "jikr");

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
    private static decimal? Total(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var total)
            ? total
            : null;

    private static string? Parameter(
        IReadOnlyDictionary<string, string> query,
        IReadOnlyDictionary<string, string> fragment,
        params string[] names) =>
        names
            .SelectMany(name => new[]
            {
                query.GetValueOrDefault(name),
                fragment.GetValueOrDefault(name),
            })
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    /// <summary>
    /// Split by hand rather than with a form decoder, because a form decoder reads '+' as a space
    /// and the creation timestamp is printed with a '+' in its UTC offset — `2026-08-29T14:59:22+02:00`
    /// would arrive as a space and the portal would be asked about an invoice created at no time at
    /// all. Percent-escapes are still decoded; '+' is left as the character it is.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Parameters(string text) =>
        text.TrimStart('?', '#')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .GroupBy(pair => Uri.UnescapeDataString(pair[0]), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => Uri.UnescapeDataString(group.First()[1]),
                StringComparer.OrdinalIgnoreCase);
}
