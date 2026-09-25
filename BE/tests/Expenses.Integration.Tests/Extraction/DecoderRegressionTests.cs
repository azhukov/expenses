using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Application.Services;
using Expenses.Domain.Entities;
using Expenses.Integration.Tests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Extraction;

/// <summary>
/// The decoder against three real photographed thermal receipts, committed as fixtures with the
/// outcome each one is known to produce (D26). Two of the three miss, and that is not a defect to
/// be tolerated quietly: the hit and the misses are asserted alike, because a decoder change that
/// silently lost the hit and one that silently rescued a miss are both things this suite must say
/// out loud.
///
/// Scenarios from receipt-ingestion: "A decodable symbol is decoded from an unmodified
/// photograph", "Decoding fails", "A miss does not degrade the receipt".
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DecoderRegressionTests(PostgresFixture postgres) : IAsyncLifetime
{
    /// <summary>The Megapromet till's symbol, and the only one of the three within reach.</summary>
    public const string DecodableReceipt = "1000023219.jpg";

    /// <summary>The Aroma till's symbols: too dense for the print quality they were printed at.</summary>
    public const string UndecodableReceipt = "1000023157.jpg";

    public const string Ikof = "32AA324CFF5030271E16D59F7F8EF636";

    public Task InitializeAsync() => postgres.Migrate();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_decodable_symbol_is_decoded_from_an_unmodified_photograph()
    {
        await using var services = postgres.Services();
        var decoder = services.GetRequiredService<IFiscalCodeDecoder>();

        // The 4000x3000 photograph exactly as the camera wrote it: no crop, no rectification, no
        // rescale. Cropping to a perfectly framed symbol was measured and bought nothing, so
        // nothing here may come to depend on it (D21).
        string? payload = await decoder.Decode(Photograph(DecodableReceipt));

        Assert.Equal(Ikof, payload is null ? null : FiscalIdentity.From(payload).Ikof);
    }

    [Theory]
    [InlineData(UndecodableReceipt)]
    [InlineData("1000023218.jpg")]
    public async Task Decoding_fails(string fixture)
    {
        await using var services = postgres.Services();
        var decoder = services.GetRequiredService<IFiscalCodeDecoder>();

        // A miss, and never an exception: the two are the same outcome to every caller, and the
        // honest expectation is that some tills produce symbols no decoder reads (D21).
        Assert.Null(await decoder.Decode(Photograph(fixture)));
    }

    [Fact]
    public async Task A_miss_does_not_degrade_the_receipt()
    {
        // The placeholder path being reached is what this scenario is about, not the real vision
        // engine's own behaviour (D33).
        await using var services = postgres.Services(("Extraction:Vision:Engine", "placeholder"));
        using var scope = services.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<ReceiptService>()
            .Capture(Photograph(UndecodableReceipt).Content);

        // Reported no differently from a receipt carrying no code at all: the stage ran, decoded
        // nothing, recorded nothing, and every later stage behaved as it always does.
        Assert.Contains("fiscal", result.Result!.StepsRun);
        Assert.Equal(Purchase.ExtractionState.Extracted, result.State);
        Assert.NotEmpty(result.Result.Candidates);
        Assert.Null(result.FailureReason);
        Assert.Null(result.Extracted.Ikof);
        Assert.Equal(FiscalInvoice.FiscalSource.None, result.FiscalSource);
    }

    public static ReceiptImageContent Photograph(string fixture) => new(
        1,
        "image/jpeg",
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)));
}
