using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Infrastructure.Receipts;
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
/// still reads neither Aroma one. So the wall D20 recorded is real but per-till, not universal вЂ”
/// and the preprocessing ladder that used to be here is gone, because the measured hit needed none
/// of it and no amount of it rescued a measured miss.
///
/// A miss is therefore an ordinary, expected outcome on some tills, and nothing may depend on a hit.
/// </summary>
internal sealed class FiscalCodeDecoder : IFiscalCodeDecoder
{
    public Task<string?> Decode(
        ReceiptImageContent image,
        CancellationToken cancellationToken = default)
    {
        // Neither format is an image ImageSharp reads; both are simply a miss, which the caller
        // already treats as ordinary.
        if (image.ContentType is ReceiptContent.Pdf or ReceiptContent.Heic)
        {
            return Task.FromResult<string?>(null);
        }

        // zxing-cpp reads luminance. Decoding straight to L8 is both what it wants and less
        // memory than a colour frame of a 12-megapixel photograph.
        using var luminance = Image.Load<L8>(image.Content);
        byte[] pixels = new byte[luminance.Width * luminance.Height];
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

        // The payload as the symbol carries it. Reading identifiers out of it happens in one place,
        // shared with the payloads a client supplies (D30).
        return Task.FromResult(decoded?.Text);
    }
}
