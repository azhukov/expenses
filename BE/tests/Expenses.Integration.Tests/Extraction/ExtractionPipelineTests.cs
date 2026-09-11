using Expenses.Application.Dtos;
using Expenses.Application.Services;
using Expenses.Domain.Entities;
using Expenses.Integration.Tests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Extraction;

/// <summary>
/// Scenarios from receipt-ingestion: "Vision extraction is pluggable and mocked in this change",
/// "Extraction lifecycle", "Extraction runs as an ordered cascade of stages", "Fiscal-code decoding
/// is opportunistic" — over the real cascade wiring rather than fakes, so the stage registration
/// itself is exercised (D20). Capture needs no purchase behind it, so these run the cascade directly.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ExtractionPipelineTests(PostgresFixture postgres) : IAsyncLifetime
{
    public Task InitializeAsync() => postgres.Migrate();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Placeholder_produces_deterministic_candidates()
    {
        await using var services = Services();
        byte[] content = Jpeg(0x71);

        var first = await Captured(services, content);
        var second = await Captured(services, content);

        // No image analysis happens: the output is derived from the content hash, so the same
        // image is the same lines every time (D12).
        Assert.Equal(
            first.Result!.Candidates.Select(candidate => (candidate.Description, candidate.Amount)),
            second.Result!.Candidates.Select(candidate => (candidate.Description, candidate.Amount)));
    }

    [Fact]
    public async Task A_different_image_produces_different_candidates()
    {
        await using var services = Services();

        var first = await Captured(services, Jpeg(0x72));
        var second = await Captured(services, Jpeg(0x73));

        Assert.NotEqual(
            first.Result!.Candidates.Select(candidate => candidate.Amount).Sum(),
            second.Result!.Candidates.Select(candidate => candidate.Amount).Sum());
    }

    [Fact]
    public async Task Placeholder_results_are_identified()
    {
        await using var services = Services();

        var view = await Captured(services, Jpeg(0x74));

        Assert.Equal("placeholder", view.Result!.EngineName);
        Assert.Equal("1.0", view.Result.EngineVersion);
    }

    [Fact]
    public async Task Stage_provenance_is_recorded()
    {
        await using var services = Services();

        var view = await Captured(services, Jpeg(0x75));

        // Which stages ran, and which stage produced each value (D20).
        Assert.Equal(["fiscal", "vision"], view.Result!.StepsRun);
        Assert.Equal("vision", view.Result.Provenance["total"]);
        Assert.All(view.Result.Candidates, candidate =>
            Assert.Equal("vision", candidate.Provenance["amount"]));
    }

    [Fact]
    public async Task Successful_extraction()
    {
        await using var services = Services();

        var view = await Captured(services, Jpeg(0x76));

        Assert.Equal(Receipt.ExtractionState.Extracted, view.State);
        Assert.True(view.Validation!.Passed);
    }

    /// <summary>
    /// Replaces "A non-reconciling result fails validation and runs the fallback", whose scenario
    /// the specs no longer carry. Arithmetic no longer decides which steps run, and there is no
    /// second tier to escalate to: a result that does not reconcile is surfaced for review with its
    /// report, and the run ends where it ended (D28).
    /// </summary>
    [Fact]
    public async Task A_non_reconciling_result_is_reviewed_rather_than_escalated()
    {
        await using var services = Services(("Extraction:Placeholder:Outcome", "NonReconciling"));

        var view = await Captured(services, Jpeg(0x77));

        Assert.Equal(Receipt.ExtractionState.NeedsReview, view.State);
        Assert.False(view.Validation!.Passed);
        Assert.Equal(["fiscal", "vision"], view.Result!.StepsRun);
    }

    [Fact]
    public async Task Low_confidence_on_a_value_arithmetic_cannot_check()
    {
        await using var services = Services(("Extraction:Placeholder:Outcome", "LowConfidence"));

        var view = await Captured(services, Jpeg(0x78));

        Assert.Equal(Receipt.ExtractionState.NeedsReview, view.State);
        Assert.True(view.Validation!.Passed);
    }

    [Fact]
    public async Task A_configured_confidence_threshold_decides_what_counts_as_low()
    {
        await using var services = Services(
            ("Extraction:Placeholder:Outcome", "LowConfidence"),
            ("Extraction:ConfidenceThreshold", "0.10"));

        var view = await Captured(services, Jpeg(0x79));

        // The same result, below a threshold nobody set low enough to care about.
        Assert.Equal(Receipt.ExtractionState.Extracted, view.State);
    }

    [Fact]
    public async Task Failed_extraction()
    {
        await using var services = Services(("Extraction:Placeholder:Outcome", "Failure"));

        var view = await Captured(services, Jpeg(0x7A));

        Assert.Equal(Receipt.ExtractionState.Failed, view.State);
        Assert.NotNull(view.FailureReason);
        Assert.Null(view.Result);
    }

    [Fact]
    public async Task Decoding_fails_and_the_outcome_is_unchanged_by_it()
    {
        await using var services = Services();

        // Nothing in these bytes is a QR code, so the decoder misses — the expected case on a
        // photographed thermal receipt (D20). Extraction must be indistinguishable from a run
        // where the stage was never configured.
        var view = await Captured(services, Jpeg(0x7B));

        Assert.Equal(Receipt.ExtractionState.Extracted, view.State);
        Assert.Null(view.Extracted.Ikof);
        Assert.Equal(Receipt.FiscalSource.None, view.FiscalSource);
        Assert.Contains("fiscal", view.Result!.StepsRun);
    }

    /// <summary>
    /// The placeholder, deterministic and free, is what these scenarios are about — not the real
    /// vision engine's own behaviour, which `ClaudeVisionExtractorTests` covers on its own (D33).
    /// </summary>
    private ServiceProvider Services(params (string Key, string Value)[] settings)
        => postgres.Services([("Extraction:Vision:Engine", "placeholder"), .. settings]);

    private static async Task<CaptureResult> Captured(IServiceProvider services, byte[] content)
    {
        using var scope = services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ReceiptService>().Capture(content);
    }

    private static byte[] Jpeg(byte seed)
        => [0xFF, 0xD8, 0xFF, 0xE0, seed, 0x4A, 0x46, 0x49, 0x46, 0x00, seed, 0x01];
}
