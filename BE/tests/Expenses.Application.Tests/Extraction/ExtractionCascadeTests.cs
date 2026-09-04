using Expenses.Application.Abstractions;
using Expenses.Application.Extraction;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain;

namespace Expenses.Application.Tests.Extraction;

/// <summary>
/// Scenarios from receipt-ingestion: "Extraction runs as an ordered cascade of stages",
/// "An extraction result is validated arithmetically", "Fiscal receipt identity is captured when
/// present", "Fiscal-code decoding is opportunistic".
/// </summary>
public sealed class ExtractionCascadeTests
{
    private static readonly ReceiptImageContent Image = new(1, "image/jpeg", [0xFF, 0xD8, 0xFF, 0x01]);

    [Fact]
    public async Task Stage_provenance_is_recorded()
    {
        var cheap = FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap"));
        var cascade = new ExtractionCascade([FakeStage.Silent("fiscal-qr", ExtractionStageRole.Opportunistic), cheap]);

        var outcome = await cascade.Run(Image);

        Assert.Equal(["fiscal-qr", "vision-cheap"], outcome.StagesRun);
        Assert.Equal(["fiscal-qr", "vision-cheap"], outcome.Result?.StagesRun);
        Assert.Equal("vision-cheap", outcome.Result?.Provenance["total"]);
        Assert.Equal("vision-cheap", outcome.Result?.Candidates[0].Provenance["amount"]);
    }

    [Fact]
    public async Task Optional_stage_produces_nothing()
    {
        var withStage = await new ExtractionCascade([
            FakeStage.Silent("fiscal-qr", ExtractionStageRole.Opportunistic),
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")),
        ]).Run(Image);

        var withoutStage = await new ExtractionCascade([
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")),
        ]).Run(Image);

        Assert.Equal(withoutStage.State, withStage.State);
        Assert.Equal(Receipt.ExtractionState.Extracted, withStage.State);
        Assert.Equal(
            withoutStage.Result?.Candidates.Select(candidate => candidate.Amount),
            withStage.Result?.Candidates.Select(candidate => candidate.Amount));
        Assert.Equal(withoutStage.Extracted, withStage.Extracted);
    }

    [Fact]
    public async Task The_expensive_stage_runs_only_on_demand()
    {
        var expensive = FakeStage.Producing(
            "vision-expensive",
            ExtractionStageRole.Fallback,
            Results.Reconciling("vision-expensive"));

        var outcome = await new ExtractionCascade([
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")),
            expensive,
        ]).Run(Image);

        Assert.Equal(0, expensive.Runs);
        Assert.Equal(Receipt.ExtractionState.Extracted, outcome.State);
        Assert.True(outcome.Validation?.Passed);
        Assert.DoesNotContain("vision-expensive", outcome.StagesRun);
    }

    [Fact]
    public async Task The_expensive_stage_runs_after_a_failed_validation()
    {
        var expensive = FakeStage.Producing(
            "vision-expensive",
            ExtractionStageRole.Fallback,
            Results.Reconciling("vision-expensive"));

        var outcome = await new ExtractionCascade([
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Failing("vision-cheap")),
            expensive,
        ]).Run(Image);

        Assert.Equal(1, expensive.Runs);
        Assert.Equal(Receipt.ExtractionState.Extracted, outcome.State);
        Assert.True(outcome.Validation?.Passed);
        Assert.Equal("vision-expensive", outcome.Result?.Provenance["total"]);
        Assert.Equal(["vision-cheap", "vision-expensive"], outcome.StagesRun);
    }

    [Fact]
    public async Task Fallback_also_fails()
    {
        // The cheap tier breaks two checks, the expensive one breaks one; the better result is the
        // one that failed fewer, and both stay distinguishable by the stage that produced them.
        var outcome = await new ExtractionCascade([
            FakeStage.Producing(
                "vision-cheap",
                ExtractionStageRole.Primary,
                Results.Failing("vision-cheap", alsoBreakDiscount: true)),
            FakeStage.Producing(
                "vision-expensive",
                ExtractionStageRole.Fallback,
                Results.Failing("vision-expensive")),
        ]).Run(Image);

        Assert.Equal(Receipt.ExtractionState.NeedsReview, outcome.State);
        Assert.Equal("vision-expensive", outcome.Result?.Provenance["total"]);
        Assert.Equal(1, outcome.Validation?.FailedCount);
        Assert.Equal(["vision-cheap", "vision-expensive"], outcome.StagesRun);
    }

    [Fact]
    public async Task The_first_result_is_kept_when_the_fallback_is_no_better()
    {
        var outcome = await new ExtractionCascade([
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Failing("vision-cheap")),
            FakeStage.Producing(
                "vision-expensive",
                ExtractionStageRole.Fallback,
                Results.Failing("vision-expensive", alsoBreakDiscount: true)),
        ]).Run(Image);

        Assert.Equal(Receipt.ExtractionState.NeedsReview, outcome.State);
        Assert.Equal("vision-cheap", outcome.Result?.Provenance["total"]);
    }

    [Fact]
    public async Task A_deterministic_result_ends_the_cascade()
    {
        var cheap = FakeStage.Producing("vision-cheap", ExtractionStageRole.Fallback, Results.Reconciling("vision-cheap"));
        var expensive = FakeStage.Producing(
            "vision-expensive",
            ExtractionStageRole.Fallback,
            Results.Reconciling("vision-expensive"));

        var outcome = await new ExtractionCascade([
            FakeStage.Decoding("fiscal-qr", new FiscalIdentifiers("d1b2c3", IssuerTaxNumber: "02365928")),
            FakeStage.Retrieving("fiscal-portal", Results.Reconciling("fiscal-portal"), new FiscalIdentifiers(Jikr: "9f8e7d")),
            cheap,
            expensive,
        ]).Run(Image);

        // No probabilistic stage runs at all behind an invoice the tax authority stated and the
        // arithmetic confirmed: asking one would be asking for a worse answer to a settled question.
        Assert.Equal(0, cheap.Runs);
        Assert.Equal(0, expensive.Runs);
        Assert.Equal(Receipt.ExtractionState.Extracted, outcome.State);
        Assert.Equal("fiscal-portal", outcome.Result?.Provenance["total"]);
        Assert.Equal(["fiscal-qr", "fiscal-portal"], outcome.StagesRun);
    }

    [Fact]
    public async Task The_placeholder_does_not_run_behind_a_retrieved_invoice()
    {
        var outcome = await new ExtractionCascade([
            FakeStage.Decoding("fiscal-qr", new FiscalIdentifiers("d1b2c3")),
            FakeStage.Retrieving("fiscal-portal", Results.Reconciling("fiscal-portal"), new FiscalIdentifiers(Jikr: "9f8e7d")),
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Fallback, Results.Reconciling("vision-cheap")),
        ]).Run(Image);

        Assert.All(
            outcome.Result!.Candidates,
            candidate => Assert.Equal("fiscal-portal", candidate.Provenance["amount"]));
    }

    [Fact]
    public async Task The_missing_identifier_arrives_from_the_verification_service()
    {
        var outcome = await new ExtractionCascade([
            FakeStage.Decoding("fiscal-qr", new FiscalIdentifiers("d1b2c3", IssuerTaxNumber: "02365928")),
            FakeStage.Retrieving("fiscal-portal", Results.Reconciling("fiscal-portal"), new FiscalIdentifiers(Jikr: "9f8e7d")),
        ]).Run(Image);

        // The identifier the code carried and the one only the service knows, held together, and
        // recorded as having come from the service rather than from the code (D24).
        Assert.Equal("d1b2c3", outcome.Extracted.Ikof);
        Assert.Equal("9f8e7d", outcome.Extracted.Jikr);
        Assert.Equal(Receipt.FiscalSource.RetrievedFromService, outcome.FiscalSource);
    }

    [Fact]
    public async Task A_failed_decode_falls_through_to_the_probabilistic_stages()
    {
        var expensive = FakeStage.Producing(
            "vision-expensive",
            ExtractionStageRole.Fallback,
            Results.Reconciling("vision-expensive"));

        var outcome = await new ExtractionCascade([
            FakeStage.Silent("fiscal-qr", ExtractionStageRole.Opportunistic),
            FakeStage.Silent("fiscal-portal", ExtractionStageRole.Primary),
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Fallback, Results.Reconciling("vision-cheap")),
            expensive,
        ]).Run(Image);

        // Exactly as a receipt carrying no code at all: the cheap tier answers, and the expensive
        // one is still held back for a failed check rather than run for a failed decode.
        Assert.Equal(Receipt.ExtractionState.Extracted, outcome.State);
        Assert.Equal("vision-cheap", outcome.Result?.Provenance["total"]);
        Assert.Equal(0, expensive.Runs);
        Assert.Equal(Receipt.FiscalSource.None, outcome.FiscalSource);
    }

    [Fact]
    public async Task A_second_fallback_tier_answers_a_failed_check()
    {
        // Both placeholder tiers are Fallback once retrieval leads the cascade (D22), so "cheap
        // first, expensive only on demand" has to survive the two sharing a role.
        var expensive = FakeStage.Producing(
            "vision-expensive",
            ExtractionStageRole.Fallback,
            Results.Reconciling("vision-expensive"));

        var outcome = await new ExtractionCascade([
            FakeStage.Silent("fiscal-portal", ExtractionStageRole.Primary),
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Fallback, Results.Failing("vision-cheap")),
            expensive,
        ]).Run(Image);

        Assert.Equal(1, expensive.Runs);
        Assert.Equal(Receipt.ExtractionState.Extracted, outcome.State);
        Assert.Equal("vision-expensive", outcome.Result?.Provenance["total"]);
    }

    [Fact]
    public async Task A_result_that_does_not_add_up_reports_the_failing_check()
    {
        var outcome = await new ExtractionCascade([
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Failing("vision-cheap")),
        ]).Run(Image);

        Assert.Equal(Receipt.ExtractionState.NeedsReview, outcome.State);
        var failure = Assert.Single(outcome.Validation!.Failures);
        Assert.Equal(ArithmeticChecks.LineSum, failure.Name);
        Assert.Equal(8.98m, failure.Values["total"]);
        Assert.Equal(8.48m, failure.Values["computedSum"]);
    }

    [Fact]
    public async Task Low_confidence_on_a_value_arithmetic_cannot_check()
    {
        var outcome = await new ExtractionCascade([
            FakeStage.Producing(
                "vision-cheap",
                ExtractionStageRole.Primary,
                Results.Reconciling(
                    "vision-cheap",
                    reportedConfidence: new Dictionary<string, decimal>
                    {
                        [ExtractedValues.MerchantName] = 0.41m,
                        [ExtractedValues.Total] = 0.20m,
                    })),
        ]).Run(Image);

        // The arithmetic passed, so only the unverifiable value moves it to review — and a
        // reported score about a number arithmetic already decided is ignored (D20).
        Assert.Equal(Receipt.ExtractionState.NeedsReview, outcome.State);
        Assert.True(outcome.Validation?.Passed);
        Assert.Equal([ExtractedValues.MerchantName], outcome.LowConfidenceValues);
    }

    [Fact]
    public async Task Failed_extraction()
    {
        var outcome = await new ExtractionCascade([
            FakeStage.Silent("vision-cheap", ExtractionStageRole.Primary),
            FakeStage.Silent("vision-expensive", ExtractionStageRole.Fallback),
        ]).Run(Image);

        Assert.Equal(Receipt.ExtractionState.Failed, outcome.State);
        Assert.Null(outcome.Result);
        Assert.NotNull(outcome.FailureReason);
    }

    [Fact]
    public async Task Decoding_succeeds()
    {
        var outcome = await new ExtractionCascade([
            FakeStage.Decoding("fiscal-qr", new FiscalIdentifiers("d1b2c3", "9f8e7d")),
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")),
        ]).Run(Image);

        Assert.Equal("d1b2c3", outcome.Extracted.Ikof);
        Assert.Equal(Receipt.FiscalSource.DecodedFromCode, outcome.FiscalSource);
        Assert.Equal(Receipt.ExtractionState.Extracted, outcome.State);
    }

    [Fact]
    public async Task Decoding_fails()
    {
        var decoded = await new ExtractionCascade([
            FakeStage.Silent("fiscal-qr", ExtractionStageRole.Opportunistic),
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")),
        ]).Run(Image);

        Assert.Equal(Receipt.ExtractionState.Extracted, decoded.State);
        Assert.True(decoded.Extracted.IsEmpty);
        Assert.Equal(Receipt.FiscalSource.None, decoded.FiscalSource);
        Assert.Null(decoded.FailureReason);
    }

    [Fact]
    public async Task Sources_disagree()
    {
        var outcome = await new ExtractionCascade([
            FakeStage.Decoding("fiscal-qr", new FiscalIdentifiers("ffffff")),
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")),
        ]).Run(Image, new FiscalIdentifiers("d1b2c3"));

        // The arithmetic passed; the disagreement alone is what moves it to review, and both
        // values survive for the reviewer to judge.
        Assert.True(outcome.Validation?.Passed);
        Assert.Equal(Receipt.ExtractionState.NeedsReview, outcome.State);
        Assert.Equal("ffffff", outcome.Extracted.Ikof);
    }

    [Fact]
    public async Task Sources_agree()
    {
        var outcome = await new ExtractionCascade([
            FakeStage.Decoding("fiscal-qr", new FiscalIdentifiers("d1b2c3")),
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")),
        ]).Run(Image, new FiscalIdentifiers("d1b2c3"));

        Assert.Equal(Receipt.ExtractionState.Extracted, outcome.State);
        Assert.Equal("d1b2c3", outcome.Extracted.Ikof);
    }

    [Fact]
    public async Task Stages_run_cheapest_first_whatever_order_they_were_registered_in()
    {
        var order = new List<string>();
        var expensive = new FakeStage("vision-expensive", ExtractionStageRole.Fallback, _ =>
        {
            order.Add("vision-expensive");
            return new ExtractionStageOutcome(Results.Reconciling("vision-expensive"));
        });
        var cheap = new FakeStage("vision-cheap", ExtractionStageRole.Primary, _ =>
        {
            order.Add("vision-cheap");
            return new ExtractionStageOutcome(Results.Failing("vision-cheap"));
        });
        var qr = new FakeStage("fiscal-qr", ExtractionStageRole.Opportunistic, _ =>
        {
            order.Add("fiscal-qr");
            return ExtractionStageOutcome.Nothing;
        });

        await new ExtractionCascade([expensive, cheap, qr]).Run(Image);

        Assert.Equal(["fiscal-qr", "vision-cheap", "vision-expensive"], order);
    }
}
