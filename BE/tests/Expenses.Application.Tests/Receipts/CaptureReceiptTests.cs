using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Application.Services;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain.Entities;

namespace Expenses.Application.Tests.Receipts;

/// <summary>
/// Scenarios from receipt-ingestion: "An image can be captured with no purchase behind it",
/// "A captured image is held temporarily until confirmed", "Extraction lifecycle", "Fiscal receipt
/// identity is captured when present".
/// </summary>
public sealed class CaptureReceiptTests
{
    private readonly InMemoryLedger _ledger = new();

    [Fact]
    public async Task Capturing_an_image_with_nothing_else_known()
    {
        var result = await Subject(FakeStage.Producing(
            "vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")))
            .Capture(Jpeg(1));

        Assert.Equal(Receipt.ExtractionState.Extracted, result.State);
        Assert.Empty(_ledger.Purchases);
    }

    [Fact]
    public async Task A_temporary_key_is_never_reused()
    {
        var subject = Subject(FakeStage.Producing(
            "vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")));

        var first = await subject.Capture(Jpeg(1));
        var second = await subject.Capture(Jpeg(1));

        Assert.NotEqual(first.TempKey, second.TempKey);
    }

    [Fact]
    public async Task State_on_capture_does_not_return_until_extraction_completes()
    {
        var result = await Subject(FakeStage.Producing(
            "vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")))
            .Capture(Jpeg(1));

        Assert.Contains(
            result.State,
            new[]
            {
                Receipt.ExtractionState.Extracted,
                Receipt.ExtractionState.NeedsReview,
                Receipt.ExtractionState.Failed,
            });
    }

    [Fact]
    public async Task Successful_extraction()
    {
        var result = await Subject(FakeStage.Producing(
            "vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")))
            .Capture(Jpeg(1));

        Assert.Equal(Receipt.ExtractionState.Extracted, result.State);
        Assert.Equal(2, result.Result?.Candidates.Count);
    }

    [Fact]
    public async Task Extraction_that_does_not_add_up()
    {
        var result = await Subject(FakeStage.Producing(
            "vision-cheap", ExtractionStageRole.Primary, Results.Failing("vision-cheap")))
            .Capture(Jpeg(1));

        Assert.Equal(Receipt.ExtractionState.NeedsReview, result.State);
        Assert.NotNull(result.Result);
    }

    [Fact]
    public async Task Failed_extraction()
    {
        var result = await Subject(FakeStage.Silent("vision-cheap", ExtractionStageRole.Primary))
            .Capture(Jpeg(1));

        Assert.Equal(Receipt.ExtractionState.Failed, result.State);
        Assert.NotNull(result.FailureReason);
        Assert.Null(result.Result);
    }

    [Fact]
    public async Task Identifiers_read_from_the_receipt()
    {
        var result = await Subject(
            FakeStage.Decoding("fiscal-qr", new FiscalIdentifiers("d1b2c3", "9f8e7d")),
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")))
            .Capture(Jpeg(1));

        Assert.Equal("d1b2c3", result.Extracted.Ikof);
        Assert.Equal("9f8e7d", result.Extracted.Jikr);
        Assert.Equal(Receipt.FiscalSource.DecodedFromCode, result.FiscalSource);
    }

    [Fact]
    public async Task Sources_disagree()
    {
        var result = await Subject(
            FakeStage.Decoding("fiscal-qr", new FiscalIdentifiers("ffffff")),
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")))
            .Capture(Jpeg(1), new FiscalIdentifiers("d1b2c3"));

        Assert.Equal(Receipt.ExtractionState.NeedsReview, result.State);
        Assert.Equal("d1b2c3", result.Supplied.Ikof);
        Assert.Equal("ffffff", result.Extracted.Ikof);
    }

    private ReceiptService Subject(params IExtractionStage[] stages)
        => new(_ledger, _ledger, _ledger, _ledger, _ledger, _ledger, new ExtractionCascade(stages), _ledger);

    private static byte[] Jpeg(byte seed) => [0xFF, 0xD8, 0xFF, seed, 0x01, 0x02];
}
