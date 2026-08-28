using System.Diagnostics;
using System.Web;
using Expenses.Application.Abstractions;
using Expenses.Application.Extraction;
using Expenses.Infrastructure.Receipts;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ZXing;
using ZXing.Common;

namespace Expenses.Infrastructure.Extraction;

internal sealed class DecoderOptions
{
    /// <summary>
    /// A hard budget, because the preprocessing ladder is unbounded work on an outcome nothing
    /// depends on. When it runs out, the decode is a miss like any other (D20).
    /// </summary>
    public int TimeBudgetMilliseconds { get; set; } = 1500;
}

/// <summary>
/// Decodes a fiscal QR from stored image bytes over a bounded preprocessing ladder (D20).
///
/// Measured before it was specified: a spike ran ZXing over a real photographed thermal receipt
/// across roughly three hundred combinations — full resolution, a downscale ladder, three crop
/// boxes, global and adaptive thresholding, morphological correction — and hit zero times. The
/// symbol is dense, printed on thermal paper where black modules bleed, photographed at about nine
/// pixels per module. That is a signal-quality wall, not a tuning problem, so the ladder here is
/// deliberately short: more combinations buy nothing on that input and cost time on every image.
///
/// It is built anyway because it costs little and yields an exactly-correct fiscal identity when it
/// does hit — usually on a flat, well-lit scan rather than a photograph. Nothing may depend on it
/// hitting, and decoding at capture in the browser is the intended primary path once `FE/` exists.
/// </summary>
internal sealed class FiscalCodeDecoder(DecoderOptions options, ILogger<FiscalCodeDecoder> logger)
    : IFiscalCodeDecoder
{
    /// <summary>
    /// Full resolution first, then two downscales. A dense symbol photographed close up sometimes
    /// decodes smaller, because downscaling averages away the ink bleed between modules.
    /// </summary>
    private static readonly double[] Scales = [1.0, 0.5, 0.35];

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

        var budget = Stopwatch.StartNew();
        var reader = new BarcodeReaderGeneric
        {
            Options = new DecodingOptions
            {
                PossibleFormats = [BarcodeFormat.QR_CODE],
                TryHarder = true,
                TryInverted = true,
            },
        };

        using var original = Image.Load<Rgba32>(image.Content);

        foreach (var scale in Scales)
        {
            if (cancellationToken.IsCancellationRequested
                || budget.ElapsedMilliseconds > options.TimeBudgetMilliseconds)
            {
                logger.LogDebug(
                    "Fiscal decoding gave up on the receipt of purchase {PurchaseId} after {Elapsed} ms.",
                    image.PurchaseId,
                    budget.ElapsedMilliseconds);

                break;
            }

            using var candidate = Rescaled(original, scale);
            if (reader.Decode(Luminance(candidate)) is { Text.Length: > 0 } decoded)
            {
                return Task.FromResult<FiscalIdentifiers?>(FiscalIdentity.From(decoded.Text));
            }
        }

        return Task.FromResult<FiscalIdentifiers?>(null);
    }

    private static Image<Rgba32> Rescaled(Image<Rgba32> original, double scale) =>
        scale >= 1.0
            ? original.Clone()
            : original.Clone(context => context.Resize(
                Math.Max(1, (int)(original.Width * scale)),
                Math.Max(1, (int)(original.Height * scale))));

    /// <summary>
    /// ZXing reads luminance, not pixels. Copying the frame into the shape it expects is cheaper
    /// than taking a dependency on a binding pinned to an older ImageSharp.
    /// </summary>
    private static RGBLuminanceSource Luminance(Image<Rgba32> image)
    {
        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);

        return new RGBLuminanceSource(pixels, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.RGBA32);
    }
}

/// <summary>
/// Reads the identifiers out of what a fiscal QR carries. The payload is a verification URL whose
/// query names them; anything else is kept as it stands, because no format is imposed on a fiscal
/// identifier anywhere in this design (D10).
/// </summary>
internal static class FiscalIdentity
{
    public static FiscalIdentifiers From(string payload)
    {
        if (!Uri.TryCreate(payload, UriKind.Absolute, out var url))
        {
            return new FiscalIdentifiers(payload.Trim());
        }

        // The identifiers live in the query of the verification URL. A fragment-routed URL — which
        // is what Montenegro's portal issues — keeps them after the '#', so both are searched.
        var query = HttpUtility.ParseQueryString(url.Query);
        var fragment = HttpUtility.ParseQueryString(
            url.Fragment.Contains('?', StringComparison.Ordinal)
                ? url.Fragment[(url.Fragment.IndexOf('?', StringComparison.Ordinal) + 1)..]
                : string.Empty);

        var ikof = query["iic"] ?? query["ikof"] ?? fragment["iic"] ?? fragment["ikof"];
        var jikr = query["crtd"] ?? query["jikr"] ?? fragment["crtd"] ?? fragment["jikr"];

        return ikof is null && jikr is null
            ? new FiscalIdentifiers(payload.Trim())
            : new FiscalIdentifiers(ikof, jikr);
    }
}
