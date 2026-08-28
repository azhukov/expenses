using Expenses.Application.Abstractions;
using Expenses.Application.Extraction;
using Expenses.Application.Purchases;
using Expenses.Application.Receipts;
using Expenses.Domain;
using Expenses.Integration.Tests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Extraction;

/// <summary>
/// Scenarios from receipt-ingestion: "Vision extraction is pluggable and mocked in this change",
/// "Extraction lifecycle", "Extraction runs as an ordered cascade of stages", "Fiscal-code decoding
/// is opportunistic" — over the real cascade wiring rather than fakes, so the stage registration
/// itself is exercised (D20).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ExtractionPipelineTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTime Occurred = new(2032, 5, 6, 18, 30, 0, DateTimeKind.Unspecified);

    /// <summary>
    /// Static, because xUnit builds a new instance per test and the unique index on
    /// <c>(occurred_at, amount)</c> would otherwise absorb every test's purchase into the first.
    /// </summary>
    private static int _sequence;

    public Task InitializeAsync() => postgres.Migrate();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Placeholder_produces_deterministic_candidates()
    {
        await using var services = postgres.Services();
        using var scope = services.CreateScope();

        var purchase = await Recorded(scope.ServiceProvider);
        await Attach(scope.ServiceProvider, purchase.Id, Jpeg(0x71));

        var first = await Run(scope.ServiceProvider, purchase.Id);
        await scope.ServiceProvider.GetRequiredService<RequeueExtraction>().Execute(purchase.Id);
        var second = await Run(scope.ServiceProvider, purchase.Id);

        // No image analysis happens: the output is derived from the content hash, so the same
        // image is the same lines every time (D12).
        Assert.Equal(
            first.Result!.Candidates.Select(candidate => (candidate.Description, candidate.Amount)),
            second.Result!.Candidates.Select(candidate => (candidate.Description, candidate.Amount)));
    }

    [Fact]
    public async Task A_different_image_produces_different_candidates()
    {
        await using var services = postgres.Services();

        var first = await Extracted(services, Jpeg(0x72));
        var second = await Extracted(services, Jpeg(0x73));

        Assert.NotEqual(
            first.Result!.Candidates.Select(candidate => candidate.Amount).Sum(),
            second.Result!.Candidates.Select(candidate => candidate.Amount).Sum());
    }

    [Fact]
    public async Task Placeholder_results_are_identified()
    {
        await using var services = postgres.Services();

        var view = await Extracted(services, Jpeg(0x74));

        Assert.Equal("placeholder", view.Result!.EngineName);
        Assert.Equal("1.0", view.Result.EngineVersion);
    }

    [Fact]
    public async Task Stage_provenance_is_recorded()
    {
        await using var services = postgres.Services();

        var view = await Extracted(services, Jpeg(0x75));

        // Which stages ran, and which stage produced each value (D20).
        Assert.Equal(["fiscal-qr", "vision-cheap"], view.Result!.StagesRun);
        Assert.Equal("vision-cheap", view.Result.Provenance["total"]);
        Assert.All(view.Result.Candidates, candidate =>
            Assert.Equal("vision-cheap", candidate.Provenance["amount"]));
    }

    [Fact]
    public async Task Successful_extraction()
    {
        await using var services = postgres.Services();

        var view = await Extracted(services, Jpeg(0x76));

        Assert.Equal(Receipt.ExtractionState.Extracted, view.Receipt.State);
        Assert.True(view.Validation!.Passed);
    }

    [Fact]
    public async Task A_non_reconciling_result_fails_validation_and_runs_the_fallback()
    {
        await using var services = postgres.Services(("Extraction:Placeholder:Outcome", "NonReconciling"));

        var view = await Extracted(services, Jpeg(0x77));

        // Both tiers are the same placeholder here, so the fallback fails in the same way: the
        // point is that a real arithmetic failure drove it, not a simulated score (D20).
        Assert.Equal(Receipt.ExtractionState.NeedsReview, view.Receipt.State);
        Assert.False(view.Validation!.Passed);
        Assert.Equal(["fiscal-qr", "vision-cheap", "vision-expensive"], view.Result!.StagesRun);
    }

    [Fact]
    public async Task Low_confidence_on_a_value_arithmetic_cannot_check()
    {
        await using var services = postgres.Services(("Extraction:Placeholder:Outcome", "LowConfidence"));

        var view = await Extracted(services, Jpeg(0x78));

        Assert.Equal(Receipt.ExtractionState.NeedsReview, view.Receipt.State);
        Assert.True(view.Validation!.Passed);
    }

    [Fact]
    public async Task A_configured_confidence_threshold_decides_what_counts_as_low()
    {
        await using var services = postgres.Services(
            ("Extraction:Placeholder:Outcome", "LowConfidence"),
            ("Extraction:ConfidenceThreshold", "0.10"));

        var view = await Extracted(services, Jpeg(0x79));

        // The same result, below a threshold nobody set low enough to care about.
        Assert.Equal(Receipt.ExtractionState.Extracted, view.Receipt.State);
    }

    [Fact]
    public async Task Failed_extraction()
    {
        await using var services = postgres.Services(("Extraction:Placeholder:Outcome", "Failure"));

        var view = await Extracted(services, Jpeg(0x7A));

        Assert.Equal(Receipt.ExtractionState.Failed, view.Receipt.State);
        Assert.NotNull(view.Receipt.FailureReason);
        Assert.Null(view.Result);
    }

    [Fact]
    public async Task Decoding_fails_and_the_outcome_is_unchanged_by_it()
    {
        await using var services = postgres.Services();

        // Nothing in these bytes is a QR code, so the decoder misses — the expected case on a
        // photographed thermal receipt (D20). Extraction must be indistinguishable from a run
        // where the stage was never configured.
        var view = await Extracted(services, Jpeg(0x7B));

        Assert.Equal(Receipt.ExtractionState.Extracted, view.Receipt.State);
        Assert.Null(view.Receipt.FiscalIkofExtracted);
        Assert.Equal(Receipt.FiscalSource.None, view.Receipt.FiscalExtractedSource);
        Assert.Contains("fiscal-qr", view.Result!.StagesRun);
    }

    [Fact]
    public async Task Upload_queues_extraction_rather_than_waiting_for_it()
    {
        await using var services = postgres.Services();
        using var scope = services.CreateScope();

        var purchase = await Recorded(scope.ServiceProvider);
        var receipt = await scope.ServiceProvider.GetRequiredService<AttachReceiptImage>()
            .Execute(purchase.Id, Jpeg(0x7C));

        Assert.Equal(Receipt.ExtractionState.Pending, receipt.State);
    }

    /// <summary>
    /// Records a purchase, attaches an image, and runs the cascade over it the way the background
    /// drain does — through the use case rather than around it.
    /// </summary>
    private async Task<ExtractionView> Extracted(IServiceProvider services, byte[] content)
    {
        using var scope = services.CreateScope();
        var purchase = await Recorded(scope.ServiceProvider);
        await Attach(scope.ServiceProvider, purchase.Id, content);

        return await Run(scope.ServiceProvider, purchase.Id);
    }

    private static async Task<ExtractionView> Run(IServiceProvider services, long purchaseId)
    {
        await services.GetRequiredService<RunExtraction>().Execute(purchaseId);

        return await services.GetRequiredService<GetExtractionCandidates>().Execute(purchaseId);
    }

    private static async Task<ReceiptView> Attach(IServiceProvider services, long purchaseId, byte[] content) =>
        await services.GetRequiredService<AttachReceiptImage>().Execute(purchaseId, content);

    private async Task<PurchaseView> Recorded(IServiceProvider services)
    {
        var occurred = Occurred.AddMinutes(Interlocked.Increment(ref _sequence));
        var result = await services.GetRequiredService<RecordPurchase>().Execute(new RecordPurchaseCommand(
            occurred,
            10.00m,
            [new ExpenseCommand("Placeholder line", 10.00m)]));

        return result.Purchase;
    }

    private static byte[] Jpeg(byte seed) =>
        [0xFF, 0xD8, 0xFF, 0xE0, seed, 0x4A, 0x46, 0x49, 0x46, 0x00, seed, 0x01];
}
