using Expenses.Application.Extraction;
using Expenses.Application.Receipts;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain;

namespace Expenses.Application.Tests.Receipts;

/// <summary>
/// Scenarios from receipt-ingestion: "Extraction lifecycle", "Extraction can be re-run",
/// "Fiscal receipt identity is captured when present", "Validation does not touch the ledger".
/// </summary>
public sealed class RunExtractionTests
{
    private static readonly DateTime Occurred = new(2026, 8, 24, 12, 50, 8, DateTimeKind.Unspecified);

    private readonly InMemoryLedger _ledger = new();

    [Fact]
    public async Task Successful_extraction()
    {
        var purchase = await GivenPendingReceipt();

        var extracted = await Subject(
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")))
            .Execute(purchase.Id);

        Assert.Equal(Receipt.ExtractionState.Extracted, extracted.State);
        Assert.Equal(2, (await _ledger.FindLatest(purchase.Id))?.Candidates.Count);

        // Extraction is a suggestion; the ledger is untouched until a user confirms (D12).
        Assert.Single(purchase.Expenses);
    }

    [Fact]
    public async Task Extraction_that_does_not_add_up()
    {
        var purchase = await GivenPendingReceipt();

        var extracted = await Subject(
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Failing("vision-cheap")))
            .Execute(purchase.Id);

        Assert.Equal(Receipt.ExtractionState.NeedsReview, extracted.State);
        Assert.NotNull(await _ledger.FindLatest(purchase.Id));
    }

    [Fact]
    public async Task Failed_extraction()
    {
        var purchase = await GivenPendingReceipt();

        var extracted = await Subject(FakeStage.Silent("vision-cheap", ExtractionStageRole.Primary))
            .Execute(purchase.Id);

        Assert.Equal(Receipt.ExtractionState.Failed, extracted.State);
        Assert.NotNull(extracted.FailureReason);
        Assert.Null(await _ledger.FindLatest(purchase.Id));

        // The purchase and its image remain intact so the user can enter lines by hand.
        Assert.Single(purchase.Expenses);
        Assert.NotNull(purchase.Receipt);
    }

    [Fact]
    public async Task Identifiers_read_from_the_receipt()
    {
        var purchase = await GivenPendingReceipt();

        var extracted = await Subject(
            FakeStage.Decoding("fiscal-qr", new FiscalIdentifiers("d1b2c3", "9f8e7d")),
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")))
            .Execute(purchase.Id);

        Assert.Equal("d1b2c3", extracted.FiscalIkofExtracted);
        Assert.Equal("9f8e7d", extracted.FiscalJikrExtracted);
        Assert.Equal(Receipt.FiscalSource.DecodedFromCode, extracted.FiscalExtractedSource);
        Assert.Equal(Receipt.FiscalCorroboration.Unverified, extracted.Corroboration);
    }

    [Fact]
    public async Task Sources_agree()
    {
        var purchase = await GivenPendingReceipt(new FiscalIdentifiers("d1b2c3"));

        var extracted = await Subject(
            FakeStage.Decoding("fiscal-qr", new FiscalIdentifiers("d1b2c3")),
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")))
            .Execute(purchase.Id);

        Assert.Equal(Receipt.FiscalCorroboration.Corroborated, extracted.Corroboration);
        Assert.Equal(Receipt.ExtractionState.Extracted, extracted.State);
    }

    [Fact]
    public async Task Sources_agree_across_a_difference_of_hyphenation()
    {
        var purchase = await GivenPendingReceipt(
            new FiscalIdentifiers(Jikr: "d2857c6a-a363-4173-bf9c-dff37f77741a"));

        var extracted = await Subject(
            FakeStage.Decoding("fiscal-qr", new FiscalIdentifiers(Jikr: "d2857c6aa3634173bf9cdff37f77741a")),
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")))
            .Execute(purchase.Id);

        // The same identifier, printed hyphenated by one ERP and unhyphenated by another. Both are
        // retained exactly as read (D10), but a difference of punctuation is not a disagreement and
        // must not put a sound receipt in front of a human (D24).
        Assert.Equal(Receipt.FiscalCorroboration.Corroborated, extracted.Corroboration);
        Assert.Equal(Receipt.ExtractionState.Extracted, extracted.State);
        Assert.Equal("d2857c6a-a363-4173-bf9c-dff37f77741a", extracted.FiscalJikrSupplied);
        Assert.Equal("d2857c6aa3634173bf9cdff37f77741a", extracted.FiscalJikrExtracted);
    }

    [Fact]
    public async Task Sources_disagree()
    {
        var purchase = await GivenPendingReceipt(new FiscalIdentifiers("d1b2c3"));

        var extracted = await Subject(
            FakeStage.Decoding("fiscal-qr", new FiscalIdentifiers("ffffff")),
            FakeStage.Producing("vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")))
            .Execute(purchase.Id);

        Assert.Equal(Receipt.FiscalCorroboration.Disagreed, extracted.Corroboration);
        Assert.Equal("d1b2c3", extracted.FiscalIkofSupplied);
        Assert.Equal("ffffff", extracted.FiscalIkofExtracted);
        Assert.Equal(Receipt.ExtractionState.NeedsReview, extracted.State);
    }

    [Fact]
    public async Task Re_run_replaces_candidates()
    {
        var purchase = await GivenPendingReceipt();
        await Subject(FakeStage.Producing(
            "vision-cheap",
            ExtractionStageRole.Primary,
            Results.Failing("vision-cheap"))).Execute(purchase.Id);

        await new RequeueExtraction(_ledger, _ledger, _ledger).Execute(purchase.Id);
        var extracted = await Subject(FakeStage.Producing(
            "vision-expensive",
            ExtractionStageRole.Primary,
            Results.Reconciling("vision-expensive"))).Execute(purchase.Id);

        var stored = await _ledger.FindLatest(purchase.Id);
        Assert.Equal(Receipt.ExtractionState.Extracted, extracted.State);
        Assert.Equal("vision-expensive", stored?.Provenance["total"]);
        Assert.Equal(2, stored?.Candidates.Count);
    }

    private RunExtraction Subject(params IExtractionStage[] stages) =>
        new(_ledger, _ledger, _ledger, new ExtractionCascade(stages), _ledger);

    private async Task<Purchase> GivenPendingReceipt(FiscalIdentifiers? supplied = null)
    {
        var purchase = _ledger.Given(Purchase.Record(Occurred, 8.48m, [Expense.Record("Groceries", 8.48m)]));
        await new AttachReceiptImage(_ledger, _ledger, _ledger, _ledger)
            .Execute(purchase.Id, [0xFF, 0xD8, 0xFF, 0x01], supplied);

        return purchase;
    }
}
