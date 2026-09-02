using Expenses.Application.Abstractions;
using Expenses.Application.Extraction;
using Expenses.Integration.Tests.Harness;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;
using ZXing.Common;


namespace Expenses.Integration.Tests.Extraction;

/// <summary>
/// Scenarios from receipt-ingestion: "Fiscal-code decoding is opportunistic" — decoding succeeds,
/// decoding fails, and an image carrying no code at all is indistinguishable from a failure (D20).
///
/// A hit is exercised against a QR code this test renders itself, because the measured result on a
/// photographed thermal receipt is a miss and a test that asserted a hit on one would be asserting
/// something the design says will not happen. The real photograph is exercised in the decoder
/// regression test, which asserts the pipeline result rather than a successful decode.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FiscalDecodingTests(PostgresFixture postgres)
{
    private const string VerificationUrl =
        "https://mapr.tax.gov.me/ic/#/verify?iic=A1B2C3D4E5F6&crtd=2026-08-24T12:50:08%2B02:00";

    [Fact]
    public async Task Decoding_succeeds()
    {
        await using var services = postgres.Services();
        var decoder = services.GetRequiredService<IFiscalCodeDecoder>();

        var decoded = await decoder.Decode(new ReceiptImageContent(1, "image/png", QrCode(VerificationUrl)));

        // The identifiers a fiscal code carries, marked by the caller as decoded rather than read
        // as text, because a decode is exact where reading printed text is not.
        //
        // This test used to assert that the JIKR was the `crtd` parameter. It is not: `crtd` is the
        // invoice creation timestamp and the JIKR is absent from the code entirely, so the
        // assertion was asserting the defect D24 records rather than the behaviour.
        Assert.Equal("A1B2C3D4E5F6", decoded?.Ikof);
        Assert.Null(decoded?.Jikr);
        Assert.Equal("2026-08-24T12:50:08+02:00", decoded?.CreatedAt);
    }

    [Fact]
    public async Task Image_carries_no_fiscal_code_at_all()
    {
        await using var services = postgres.Services();
        var decoder = services.GetRequiredService<IFiscalCodeDecoder>();

        var decoded = await decoder.Decode(new ReceiptImageContent(1, "image/png", Blank()));

        // Indistinguishable from a code that could not be decoded: both are simply nothing.
        Assert.Null(decoded);
    }

    [Fact]
    public async Task A_format_the_decoder_cannot_read_is_a_miss_rather_than_an_error()
    {
        await using var services = postgres.Services();
        var decoder = services.GetRequiredService<IFiscalCodeDecoder>();

        var decoded = await decoder.Decode(new ReceiptImageContent(1, "application/pdf", [.. "%PDF-1.7"u8]));

        Assert.Null(decoded);
    }

    [Fact]
    public async Task A_decoder_that_throws_is_a_decoder_that_missed()
    {
        await using var services = postgres.Services();
        var stages = services.GetServices<IExtractionStage>();
        var stage = stages.Single(candidate => candidate.Role == ExtractionStageRole.Opportunistic);

        // Bytes that are not an image at all: the decoder cannot even load them. Nothing depends
        // on this stage hitting, so its failure must not become the extraction's (D20).
        var outcome = await stage.Run(new ExtractionStageRequest(
            new ReceiptImageContent(1, "image/png", [0x01, 0x02, 0x03]),
            FiscalIdentifiers.None));

        Assert.True(outcome.ProducedNothing);
    }

    /// <summary>
    /// Renders the symbol from ZXing's own bit matrix, so the test depends on nothing beyond the
    /// encoder the decoder is paired with.
    /// </summary>
    private static byte[] QrCode(string payload)
    {
        var matrix = new BarcodeWriterGeneric
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = 600, Height = 600, Margin = 4 },
        }.Encode(payload);

        using var image = new Image<Rgba32>(matrix.Width, matrix.Height);
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
            {
                image[x, y] = matrix[x, y] ? Color.Black.ToPixel<Rgba32>() : Color.White.ToPixel<Rgba32>();
            }
        }

        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());

        return buffer.ToArray();
    }

    private static byte[] Blank()
    {
        using var image = new Image<Rgba32>(200, 200, Color.White.ToPixel<Rgba32>());
        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());

        return buffer.ToArray();
    }
}
