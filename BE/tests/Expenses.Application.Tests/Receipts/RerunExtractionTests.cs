using Expenses.Application.Dtos;
using Expenses.Application.Errors;
using Expenses.Application.Interfaces;
using Expenses.Application.Services;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain.Entities;

namespace Expenses.Application.Tests.Receipts;

/// <summary>Scenarios from receipt-ingestion: "Extraction can be re-run".</summary>
public sealed class RerunExtractionTests
{
    private static readonly DateTime s_occurred = new(2026, 8, 24, 12, 50, 8, DateTimeKind.Unspecified);

    private readonly InMemoryLedger _ledger = new();

    /// <summary>A confirmed line needs a unit, whatever the test is really about.</summary>
    public RerunExtractionTests() => _ledger.Given(Unit.Create("PCS", "Piece", "pcs", Unit.UnitKind.Count));

    [Fact]
    public async Task Re_run_replaces_candidates()
    {
        var purchase = GivenPurchaseWithReceipt(Purchase.ExtractionState.NeedsReview);

        var extracted = await Subject(FakeStep.Producing("vision", Results.Reconciling("vision"))).RerunExtraction(purchase.Id);

        var stored = await _ledger.FindLatest(purchase.Id);
        Assert.Equal(Purchase.ExtractionState.Extracted, extracted.State);
        Assert.Equal("vision", stored?.Provenance["total"]);
        Assert.Equal(2, stored?.Candidates.Count);
    }

    [Fact]
    public async Task Re_run_after_confirmation()
    {
        var purchase = GivenPurchaseWithReceipt(Purchase.ExtractionState.Extracted);
        await Subject().ConfirmCandidates(purchase.Id, [new ExpenseCommand("Sladoled", 8.48m, UnitCode: "PCS")]);

        var extracted = await Subject(FakeStep.Producing(
            "vision", Results.Reconciling("vision")))
            .RerunExtraction(purchase.Id);

        Assert.Equal(Purchase.ExtractionState.Extracted, extracted.State);
        Assert.Equal(["Sladoled"], purchase.Expenses.Select(expense => expense.Description));
    }

    [Fact]
    public async Task Re_run_a_failed_extraction()
    {
        var purchase = GivenPurchaseWithReceipt(Purchase.ExtractionState.Failed, "engine returned no result");

        var extracted = await Subject(FakeStep.Producing(
            "vision", Results.Reconciling("vision")))
            .RerunExtraction(purchase.Id);

        Assert.Equal(Purchase.ExtractionState.Extracted, extracted.State);
        Assert.Null(extracted.FailureReason);
    }

    [Fact]
    public async Task Re_running_a_purchase_without_a_receipt_is_reported()
    {
        var purchase = _ledger.Given(Purchase.Record(s_occurred, 8.48m, [Expense.Record("Groceries", 8.48m)]));

        var error = await Assert.ThrowsAsync<ExpensesException>(() =>
            Subject().RerunExtraction(purchase.Id));

        Assert.Equal(ApplicationErrors.ReceiptImageNotFound, error.Error.Code);
    }

    [Fact]
    public async Task Re_running_a_purchase_that_does_not_exist_is_reported()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject().RerunExtraction(4711));

        Assert.Equal(ApplicationErrors.PurchaseNotFound, error.Error.Code);
    }

    /// <summary>receipt-ingestion, "Extraction can be re-run": "Re-run with no image".</summary>
    [Fact]
    public async Task Re_run_with_no_image()
    {
        const string Payload = "https://mapr.tax.gov.me/ic/#/verify?iic=32AA324CFF5030271E16D59F7F8EF636&tin=02365928";
        var invoice = FiscalInvoice.Create();
        invoice.RecordPayload(Payload, FiscalInvoice.FiscalSource.SuppliedAtUpload);
        var purchase = _ledger.Given(Purchase.Record(
            s_occurred,
            8.48m,
            [Expense.Record("Groceries", 8.48m)],
            fiscal: invoice,
            extraction: Purchase.ExtractionState.Failed,
            extractionFailureReason: "the service could not be reached"));

        var fiscal = FakeStep.Retrieving("fiscal", Results.Reconciling("fiscal"), FiscalIdentifiers.None);
        var vision = FakeStep.Producing("vision", Results.Reconciling("vision")).ReadingImage();

        var extracted = await Subject(fiscal, vision).RerunExtraction(purchase.Id);

        Assert.Equal(Purchase.ExtractionState.Extracted, extracted.State);
        Assert.Equal(Payload, fiscal.LastRequest?.Payload);
        Assert.Null(fiscal.LastRequest?.Image);
        Assert.Equal(0, vision.Runs);
        Assert.Null(extracted.ContentType);
    }

    private ReceiptService Subject(params IExtractionStep[] steps)
        => new(_ledger, _ledger, _ledger, _ledger, _ledger, _ledger, new ExtractionCascade(steps), _ledger);

    private Purchase GivenPurchaseWithReceipt(Purchase.ExtractionState state, string? failureReason = null)
    {
        var stored = _ledger.GivenReceiptFile(Jpeg(1));

        return _ledger.Given(Purchase.Record(
            s_occurred,
            8.48m,
            [Expense.Record("Groceries", 8.48m)],
            receipt: stored.AsReceipt(),
            extraction: state,
            extractionFailureReason: failureReason));
    }

    private static byte[] Jpeg(byte seed) => [0xFF, 0xD8, 0xFF, seed, 0x01, 0x02];
}
