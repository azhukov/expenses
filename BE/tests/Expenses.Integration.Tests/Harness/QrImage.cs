using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;
using ZXing.Common;

namespace Expenses.Integration.Tests.Harness;

/// <summary>
/// Renders a QR code the decode stage can actually read. Used where a test needs a hit rather than
/// the miss a photographed thermal receipt reliably produces (D20).
/// </summary>
public static class QrImage
{
    public static byte[] Png(string payload)
    {
        var matrix = new BarcodeWriterGeneric
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = 600, Height = 600, Margin = 4 },
        }.Encode(payload);

        using var image = new Image<Rgba32>(matrix.Width, matrix.Height);
        var black = Color.Black.ToPixel<Rgba32>();
        var white = Color.White.ToPixel<Rgba32>();

        for (int y = 0; y < matrix.Height; y++)
        {
            for (int x = 0; x < matrix.Width; x++)
            {
                image[x, y] = matrix[x, y] ? black : white;
            }
        }

        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());

        return buffer.ToArray();
    }
}
