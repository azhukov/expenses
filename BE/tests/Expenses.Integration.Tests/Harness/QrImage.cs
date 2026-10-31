using ImageMagick;
using ZXing;
using ZXing.Common;

namespace Expenses.Integration.Tests.Harness;

/// <summary>
/// Renders a QR code the decode stage can actually read. Used where a test needs a hit rather than
/// the miss a photographed thermal receipt reliably produces (D20).
/// </summary>
public static class QrImage
{
    /// <summary>
    /// Renders the symbol from ZXing's own bit matrix, so a test depends on nothing beyond the
    /// encoder the decoder is paired with.
    /// </summary>
    public static byte[] Png(string payload)
    {
        var matrix = new BarcodeWriterGeneric
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = 600, Height = 600, Margin = 4 },
        }.Encode(payload);

        byte[] pixels = new byte[matrix.Width * matrix.Height * 3];
        for (int y = 0; y < matrix.Height; y++)
        {
            for (int x = 0; x < matrix.Width; x++)
            {
                byte shade = matrix[x, y] ? (byte)0 : (byte)255;
                int offset = ((y * matrix.Width) + x) * 3;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = shade;
            }
        }

        using var image = new MagickImage(
            pixels,
            new PixelReadSettings((uint)matrix.Width, (uint)matrix.Height, StorageType.Char, PixelMapping.RGB));

        return image.ToByteArray(MagickFormat.Png);
    }
}
