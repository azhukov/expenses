using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Infrastructure.Receipts;
using ImageMagick;
using ZXingCpp;

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
        // The reader is pinned to the format the bytes were sniffed as on arrival, so ImageMagick
        // never picks a coder of its own from user-supplied content. A PDF is a miss: rendering one
        // would need Ghostscript in the image, and nothing may depend on a hit anyway.
        MagickFormat? format = image.ContentType switch
        {
            ReceiptContent.Jpeg => MagickFormat.Jpeg,
            ReceiptContent.Png => MagickFormat.Png,
            ReceiptContent.WebP => MagickFormat.WebP,
            ReceiptContent.Heic => MagickFormat.Heic,
            _ => null,
        };

        if (format is null)
        {
            return Task.FromResult<string?>(null);
        }

        // zxing-cpp reads luminance, so the frame is reduced to a single grey channel before its
        // pixels are copied out.
        using var luminance = new MagickImage(image.Content, new MagickReadSettings { Format = format.Value });
        luminance.Grayscale();
        int width = (int)luminance.Width;
        int height = (int)luminance.Height;
        using var pixelCollection = luminance.GetPixels();
        byte[] pixels = pixelCollection.ToByteArray("R")
            ?? throw new InvalidOperationException("ImageMagick returned no pixels for a decoded image.");

        var reader = new BarcodeReader
        {
            Formats = BarcodeFormat.QRCode,
            TryHarder = true,
            TryRotate = true,
            TryInvert = true,
            TryDownscale = true,
        };

        var decoded = reader
            .From(new ImageView(pixels, width, height, ImageFormat.Lum, 0, 0))
            .FirstOrDefault(barcode => barcode.IsValid && barcode.Text.Length > 0);

        // The payload as the symbol carries it. Reading identifiers out of it happens in one place,
        // shared with the payloads a client supplies (D30).
        return Task.FromResult(decoded?.Text);
    }
}
