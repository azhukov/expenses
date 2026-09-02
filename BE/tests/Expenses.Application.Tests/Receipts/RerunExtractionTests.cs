using Expenses.Application.Errors;
using Expenses.Application.Extraction;
using Expenses.Application.Purchases;
using Expenses.Application.Receipts;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain;

namespace Expenses.Application.Tests.Receipts;

/// <summary>Scenarios from receipt-ingestion: "Extraction can be re-run".</summary>
public sealed class RerunExtractionTests
{
    private static readonly DateTime Occurred = new(2026, 8, 24, 12, 50, 8, DateTimeKind.Unspecified);

    private readonly InMemoryLedger _ledger = new();

    [Fact]
    public async Task Re_run_replaces_candidates()
    {
        var purchase = GivenPurchaseWithReceipt(Receipt.ExtractionState.NeedsReview);

        var extracted = await Subject(FakeStage.Producing(
            "vision-expensive",
            ExtractionStageRole.Primary,
            Results.Reconciling("vision-expensive"))).Execute(purchase.Id);

        var stored = await _ledger.FindLatest(purchase.Id);
        Assert.Equal(Receipt.ExtractionState.Extracted, extracted.State);
        Assert.Equal("vision-expensive", stored?.Provenance["total"]);
        Assert.Equal(2, stored?.Candidates.Count);
    }

    [Fact]
    public async Task Re_run_after_confirmation()
    {
        var purchase = GivenPurchaseWithReceipt(Receipt.ExtractionState.Extracted);
        await new ConfirmCandidates(_ledger, _ledger, _ledger, _ledger, _ledger)
            .Execute(purchase.Id, [new ExpenseCommand("Sladoled", 8.48m)]);

        var extracted = await Subject(FakeStage.Producing(
            "vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")))
            .Execute(purchase.Id);

        Assert.Equal(Receipt.ExtractionState.Extracted, extracted.State);
        Assert.Equal(["Sladoled"], purchase.Expenses.Select(expense => expense.Description));
    }

    [Fact]
    public async Task Re_run_a_failed_extraction()
    {
        var purchase = GivenPurchaseWithReceipt(Receipt.ExtractionState.Failed, "engine returned no result");

        var extracted = await Subject(FakeStage.Producing(
            "vision-cheap", ExtractionStageRole.Primary, Results.Reconciling("vision-cheap")))
            .Execute(purchase.Id);

        Assert.Equal(Receipt.ExtractionState.Extracted, extracted.State);
        Assert.Null(extracted.FailureReason);
    }

    [Fact]
    public async Task Re_running_a_purchase_without_a_receipt_is_reported()
    {
        var purchase = _ledger.Given(Purchase.Record(Occurred, 8.48m, [Expense.Record("Groceries", 8.48m)]));

        var error = await Assert.ThrowsAsync<ExpensesException>(() =>
            Subject().Execute(purchase.Id));

        Assert.Equal(ApplicationErrors.ReceiptImageNotFound, error.Error.Code);
    }

    [Fact]
    public async Task Re_running_a_purchase_that_does_not_exist_is_reported()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject().Execute(4711));

        Assert.Equal(ApplicationErrors.PurchaseNotFound, error.Error.Code);
    }

    private RerunExtraction Subject(params IExtractionStage[] stages) =>
        new(_ledger, _ledger, _ledger, new ExtractionCascade(stages), _ledger);

    private Purchase GivenPurchaseWithReceipt(Receipt.ExtractionState state, string? failureReason = null)
    {
        var stored = _ledger.GivenReceiptFile(Jpeg(1));

        return _ledger.Given(Purchase.Record(
            Occurred,
            8.48m,
            [Expense.Record("Groceries", 8.48m)],
            receipt: stored.AsReceipt(state, failureReason)));
    }

    private static byte[] Jpeg(byte seed) => [0xFF, 0xD8, 0xFF, seed, 0x01, 0x02];
}
