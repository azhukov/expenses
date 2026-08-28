using System.Diagnostics;
using Expenses.Application.Abstractions;
using Expenses.Application.Extraction;
using Expenses.Application.Purchases;
using Expenses.Application.Receipts;
using Expenses.Domain;
using Expenses.Integration.Tests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Extraction;

/// <summary>
/// The decoder against a real photographed thermal receipt — the input the spike measured at zero
/// hits over roughly three hundred preprocessing combinations (D20).
///
/// What is asserted is the documented behaviour, not a successful decode: whatever the decoder
/// makes of this image, the pipeline result is the same. Asserting a hit would encode a hope, and
/// asserting a miss would freeze a limitation that a better decoder should be free to lift. Both
/// outcomes pass here; neither may change the state of the image, the candidates, or what a later
/// stage does.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DecoderRegressionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTime Occurred = new(2039, 10, 11, 9, 30, 0, DateTimeKind.Unspecified);

    private static int _sequence;

    public Task InitializeAsync() => postgres.Migrate();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Decoding_a_photographed_receipt_costs_little_and_never_throws()
    {
        await using var services = postgres.Services();
        var decoder = services.GetRequiredService<IFiscalCodeDecoder>();

        var elapsed = Stopwatch.StartNew();
        var decoded = await decoder.Decode(new ReceiptImageContent(1, "image/jpeg", Photograph()));
        elapsed.Stop();

        // A miss is the expected outcome and a hit is welcome; neither is an error, and the budget
        // is what keeps an unbounded ladder from costing an unbounded amount.
        Assert.True(
            elapsed.ElapsedMilliseconds < 10_000,
            $"Decoding took {elapsed.ElapsedMilliseconds} ms, well beyond its time budget.");

        if (decoded is not null)
        {
            Assert.False(decoded.IsEmpty);
        }
    }

    [Fact]
    public async Task The_pipeline_result_is_the_same_whatever_the_decoder_makes_of_the_photograph()
    {
        await using var services = postgres.Services();
        using var scope = services.CreateScope();

        var purchase = await scope.ServiceProvider.GetRequiredService<RecordPurchase>().Execute(
            new RecordPurchaseCommand(Next(), 8.48m, [new ExpenseCommand("Receipt", 8.48m)]));

        await scope.ServiceProvider.GetRequiredService<AttachReceiptImage>()
            .Execute(purchase.Purchase.Id, Photograph());

        var purchaseId = purchase.Purchase.Id;
        var extracted = await scope.ServiceProvider.GetRequiredService<RunExtraction>().Execute(purchaseId);
        var view = await scope.ServiceProvider.GetRequiredService<GetExtractionCandidates>().Execute(purchaseId);

        // The stage ran, and whether it decoded anything or not, the extraction reached its normal
        // outcome with candidates to review.
        Assert.Contains("fiscal-qr", view.Result!.StagesRun);
        Assert.Equal(Receipt.ExtractionState.Extracted, extracted.State);
        Assert.NotEmpty(view.Result.Candidates);
        Assert.Null(extracted.FailureReason);

        // If it missed — the measured outcome — nothing is recorded and nothing is reported as
        // wrong with the receipt.
        if (extracted.FiscalIkofExtracted is null)
        {
            Assert.Equal(Receipt.FiscalSource.None, extracted.FiscalExtractedSource);
            Assert.Equal(Receipt.FiscalCorroboration.Absent, extracted.Corroboration);
        }
    }

    private static byte[] Photograph() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "photographed-receipt.jpg"));

    private static DateTime Next() => Occurred.AddMinutes(Interlocked.Increment(ref _sequence));
}
