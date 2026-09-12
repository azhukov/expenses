using Expenses.Application.Dtos;
using Expenses.Application.Services;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain.Entities;

namespace Expenses.Application.Tests.Extraction;

/// <summary>
/// Scenarios from receipt-ingestion, "Extraction runs as an ordered cascade of stages": step
/// provenance, a step that reads lines ending the run, a step that reads none advancing it, a
/// result that does not reconcile not advancing it, fiscal identity outliving the step that found
/// it, and no step producing anything.
/// </summary>
public sealed class ExtractionCascadeTests
{
    private static readonly ReceiptImageContent s_image = new(1, "image/jpeg", [0xFF, 0xD8, 0xFF, 0x01]);

    private static readonly FiscalIdentifiers s_decoded = new(
        "32AA324CFF5030271E16D59F7F8EF636",
        Jikr: null,
        IssuerTaxNumber: "02365928",
        CreatedAt: "2026-08-29T14:59:22+02:00",
        Total: 59.65m);

    [Fact]
    public async Task Step_provenance_is_recorded()
    {
        var vision = FakeStep.Producing("vision", Results.Reconciling("vision"));
        var outcome = await new ExtractionCascade([FakeStep.Silent("fiscal"), vision]).Run(s_image);

        Assert.Equal(["fiscal", "vision"], outcome.StepsRun);
        Assert.Equal(["fiscal", "vision"], outcome.Result?.StepsRun);
        Assert.Equal("vision", outcome.Result?.Provenance["total"]);
        Assert.Equal("vision", outcome.Result?.Candidates[0].Provenance["amount"]);
    }

    [Fact]
    public async Task A_step_that_produces_lines_ends_the_run()
    {
        var vision = FakeStep.Producing("vision", Results.Reconciling("vision"));

        var outcome = await new ExtractionCascade([
            FakeStep.Producing("fiscal", Results.Reconciling("fiscal")),
            vision,
        ]).Run(s_image);

        Assert.Equal(0, vision.Runs);
        Assert.Equal(["fiscal"], outcome.StepsRun);
    }

    [Fact]
    public async Task A_step_that_produces_nothing_advances_the_run()
    {
        var withStep = await new ExtractionCascade([
            FakeStep.Silent("fiscal"),
            FakeStep.Producing("vision", Results.Reconciling("vision")),
        ]).Run(s_image);

        var withoutStep = await new ExtractionCascade([
            FakeStep.Producing("vision", Results.Reconciling("vision")),
        ]).Run(s_image);

        // The outcome is the same as if the step had not been configured at all (D20).
        Assert.Equal(
            withoutStep.Result?.Candidates.Select(candidate => candidate.Amount),
            withStep.Result?.Candidates.Select(candidate => candidate.Amount));
        Assert.Equal(withoutStep.Extracted, withStep.Extracted);
        Assert.Null(withStep.FailureReason);
    }

    /// <summary>
    /// The rule this whole change turns on. Arithmetic used to decide whether to continue, so a
    /// result the tax authority itself stated could be handed to a probabilistic engine to be
    /// re-guessed over a rounding difference. It now decides nothing about the run (D28).
    /// </summary>
    [Fact]
    public async Task A_result_that_does_not_reconcile_does_not_advance_the_run()
    {
        var vision = FakeStep.Producing("vision", Results.Reconciling("vision"));

        var outcome = await new ExtractionCascade([
            FakeStep.Producing("fiscal", Results.Failing("fiscal")),
            vision,
        ]).Run(s_image);

        Assert.Equal(0, vision.Runs);
        Assert.Equal("fiscal", outcome.Result?.Provenance["total"]);
        Assert.Equal(["fiscal"], outcome.StepsRun);
    }

    [Fact]
    public async Task Fiscal_identity_outlives_the_step_that_established_it()
    {
        var vision = FakeStep.Producing("vision", Results.Reconciling("vision"));

        var outcome = await new ExtractionCascade([
            FakeStep.FiscalOnly("fiscal", s_decoded, "https://mapr.tax.gov.me/ic/#/verify?iic=32AA"),
            vision,
        ]).Run(s_image);

        // Passed to the step behind it: a known-true total and issuer identity are worth having
        // even where the verification service never answered (D23, D29).
        Assert.Equal(s_decoded.Ikof, vision.LastRequest?.Fiscal.Ikof);
        Assert.Equal(59.65m, vision.LastRequest?.Fiscal.Total);
        Assert.Equal("https://mapr.tax.gov.me/ic/#/verify?iic=32AA", vision.LastRequest?.Payload);

        // And retained on the result the later step produced.
        Assert.Equal(s_decoded.Ikof, outcome.Extracted.Ikof);
        Assert.Equal(s_decoded.Ikof, outcome.Result?.Fiscal.Ikof);
        Assert.Equal(Receipt.FiscalSource.DecodedFromCode, outcome.FiscalSource);
    }

    [Fact]
    public async Task No_step_produces_anything()
    {
        var outcome = await new ExtractionCascade([
            FakeStep.Silent("fiscal"),
            FakeStep.Silent("vision"),
        ]).Run(s_image);

        Assert.Null(outcome.Result);
        Assert.NotNull(outcome.FailureReason);
        Assert.Equal(["fiscal", "vision"], outcome.StepsRun);
    }

    [Fact]
    public async Task A_failed_decode_falls_through_to_the_probabilistic_step()
    {
        var withCode = await new ExtractionCascade([
            FakeStep.FiscalOnly("fiscal", s_decoded),
            FakeStep.Producing("vision", Results.Reconciling("vision")),
        ]).Run(s_image);

        var withoutCode = await new ExtractionCascade([
            FakeStep.Silent("fiscal"),
            FakeStep.Producing("vision", Results.Reconciling("vision")),
        ]).Run(s_image);

        // The lines are the same either way: nothing about reading the receipt depends on the code.
        Assert.Equal(
            withCode.Result?.Candidates.Select(candidate => candidate.Amount),
            withoutCode.Result?.Candidates.Select(candidate => candidate.Amount));
    }

    [Fact]
    public async Task The_missing_identifier_arrives_from_the_verification_service()
    {
        var outcome = await new ExtractionCascade([
            FakeStep.Retrieving(
                "fiscal",
                Results.Reconciling("fiscal"),
                s_decoded with { Jikr = "9F8E7D6C" }),
            FakeStep.Silent("vision"),
        ]).Run(s_image);

        // The JIKR is absent from the code and printed nowhere the server can read it, so a receipt
        // whose identity the service completed records that it did (D24).
        Assert.Equal("9F8E7D6C", outcome.Extracted.Jikr);
        Assert.Equal(Receipt.FiscalSource.RetrievedFromService, outcome.FiscalSource);
    }

    /// <summary>
    /// What the caller supplied is threaded to the steps and carried on the outcome, but is not
    /// reported as extracted: echoing a reading back as a second one would make every supplied
    /// identifier look corroborated by itself.
    /// </summary>
    [Fact]
    public async Task A_supplied_reading_is_not_reported_as_a_second_one()
    {
        var vision = FakeStep.Producing("vision", Results.Reconciling("vision"));

        var outcome = await new ExtractionCascade([FakeStep.Silent("fiscal"), vision])
            .Run(s_image, s_decoded, "https://mapr.tax.gov.me/ic/#/verify?iic=32AA");

        // Threaded to the step behind it, and carried on the outcome.
        Assert.Equal(s_decoded.Ikof, vision.LastRequest?.Fiscal.Ikof);
        Assert.Equal("https://mapr.tax.gov.me/ic/#/verify?iic=32AA", outcome.Payload);
        Assert.Equal("https://mapr.tax.gov.me/ic/#/verify?iic=32AA", vision.LastRequest?.Payload);

        // But nothing established it here, so nothing is reported as extracted.
        Assert.Null(outcome.Extracted.Ikof);
        Assert.Equal(Receipt.FiscalSource.None, outcome.FiscalSource);
    }

    [Fact]
    public async Task Steps_run_in_registration_order()
    {
        var outcome = await new ExtractionCascade([
            FakeStep.Silent("first"),
            FakeStep.Silent("second"),
            FakeStep.Producing("third", Results.Reconciling("third")),
        ]).Run(s_image);

        Assert.Equal(["first", "second", "third"], outcome.StepsRun);
    }
}
