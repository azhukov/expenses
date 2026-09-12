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
        var result = await Subject(FakeStep.Producing(
            "vision", Results.Reconciling("vision")))
            .Capture(Jpeg(1));

        Assert.Equal(Receipt.ExtractionState.Extracted, result.State);
        Assert.Empty(_ledger.Purchases);
    }

    [Fact]
    public async Task A_temporary_key_is_never_reused()
    {
        var subject = Subject(FakeStep.Producing(
            "vision", Results.Reconciling("vision")));

        var first = await subject.Capture(Jpeg(1));
        var second = await subject.Capture(Jpeg(1));

        Assert.NotEqual(first.TempKey, second.TempKey);
    }

    [Fact]
    public async Task State_on_capture_does_not_return_until_extraction_completes()
    {
        var result = await Subject(FakeStep.Producing(
            "vision", Results.Reconciling("vision")))
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
        var result = await Subject(FakeStep.Producing(
            "vision", Results.Reconciling("vision")))
            .Capture(Jpeg(1));

        Assert.Equal(Receipt.ExtractionState.Extracted, result.State);
        Assert.Equal(2, result.Result?.Candidates.Count);
    }

    [Fact]
    public async Task Extraction_that_does_not_add_up()
    {
        var result = await Subject(FakeStep.Producing(
            "vision", Results.Failing("vision")))
            .Capture(Jpeg(1));

        Assert.Equal(Receipt.ExtractionState.NeedsReview, result.State);
        Assert.NotNull(result.Result);
    }

    [Fact]
    public async Task Failed_extraction()
    {
        var result = await Subject(FakeStep.Silent("vision"))
            .Capture(Jpeg(1));

        Assert.Equal(Receipt.ExtractionState.Failed, result.State);
        Assert.NotNull(result.FailureReason);
        Assert.Null(result.Result);
    }

    [Fact]
    public async Task Identifiers_read_from_the_receipt()
    {
        var result = await Subject(
            FakeStep.FiscalOnly("fiscal", new FiscalIdentifiers("d1b2c3", "9f8e7d")),
            FakeStep.Producing("vision", Results.Reconciling("vision")))
            .Capture(Jpeg(1));

        Assert.Equal("d1b2c3", result.Extracted.Ikof);
        Assert.Equal("9f8e7d", result.Extracted.Jikr);
        Assert.Equal(Receipt.FiscalSource.DecodedFromCode, result.FiscalSource);
    }

    /// <summary>
    /// A supplied payload is preferred outright rather than cross-checked, so the image is never
    /// read for a second opinion and there is no disagreement to report (D31). This replaces the
    /// "Sources disagree" test, whose scenario the specs no longer carry.
    /// </summary>
    [Fact]
    public async Task A_supplied_reading_is_preferred_outright()
    {
        const string Payload = "https://mapr.tax.gov.me/ic/#/verify?iic=d1b2c3";

        var result = await Subject(
            FakeStep.Silent("fiscal"),
            FakeStep.Producing("vision", Results.Reconciling("vision")))
            .Capture(Jpeg(1), new FiscalIdentifiers("d1b2c3"), Payload);

        Assert.Equal("d1b2c3", result.Supplied.Ikof);

        // Carried back for the caller to resubmit at confirmation, where the receipt retains it.
        Assert.Equal(Payload, result.FiscalPayload);

        // Nothing read the image for a second opinion, so there is no extracted reading to
        // contradict the supplied one.
        Assert.Null(result.Extracted.Ikof);
    }

    private ReceiptService Subject(params IExtractionStep[] steps)
        => new(_ledger, _ledger, _ledger, _ledger, _ledger, _ledger, new ExtractionCascade(steps), _ledger);

    private static byte[] Jpeg(byte seed) => [0xFF, 0xD8, 0xFF, seed, 0x01, 0x02];
}
