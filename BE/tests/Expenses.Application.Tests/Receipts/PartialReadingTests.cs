using Expenses.Application.Dtos;
using Expenses.Application.Errors;
using Expenses.Application.Interfaces;
using Expenses.Application.Services;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain.Entities;
using Expenses.Domain.Extraction;

namespace Expenses.Application.Tests.Receipts;

/// <summary>
/// A receipt photographed badly: whatever could be read reaches review and everything else is left
/// empty, never invented. An incomplete reading is always a person's to finish, and confirming it
/// unedited is refused where a line has no amount to assert.
/// </summary>
public sealed class PartialReadingTests
{
    private static readonly DateTime s_occurred = new(2026, 8, 29, 14, 59, 22, DateTimeKind.Unspecified);

    private readonly InMemoryLedger _ledger = new();

    [Fact]
    public async Task A_line_read_without_its_amount_is_kept_for_review()
    {
        var result = await Subject(Vision(
            ExtractionCandidate.Propose(1, "Bread", 2.50m),
            ExtractionCandidate.Propose(2, "Milk", amount: null)))
            .Capture(Jpeg(1));

        Assert.Equal(Purchase.ExtractionState.NeedsReview, result.State);
        Assert.NotNull(result.Result);
        Assert.Equal([2.50m, null], result.Result.Candidates.Select(candidate => candidate.Amount));
    }

    [Fact]
    public async Task A_line_read_without_its_description_is_kept_for_review()
    {
        var result = await Subject(Vision(ExtractionCandidate.Propose(1, description: null, 2.50m)))
            .Capture(Jpeg(1));

        Assert.Equal(Purchase.ExtractionState.NeedsReview, result.State);
        Assert.NotNull(result.Result);
        Assert.Null(Assert.Single(result.Result.Candidates).Description);
    }

    [Fact]
    public async Task A_reading_with_no_lines_is_kept_for_review()
    {
        var headerOnly = ExtractionStepResult.From(0, "vision", "1.0", [], total: 12.40m, merchantName: "Blurred Market");

        var result = await Subject(FakeStep.Producing("vision", headerOnly).ReadingImage()).Capture(Jpeg(1));

        Assert.Equal(Purchase.ExtractionState.NeedsReview, result.State);
        Assert.Null(result.FailureReason);
        Assert.Equal("Blurred Market", result.Result?.MerchantName);
        Assert.Equal(12.40m, result.Result?.Total);
    }

    [Fact]
    public async Task Confirming_unedited_a_line_without_an_amount_is_refused()
    {
        var purchase = _ledger.Given(Purchase.Record(
            s_occurred,
            2.50m,
            [Expense.Record("Groceries", 2.50m)],
            receipt: _ledger.GivenReceiptFile([0xFF, 0xD8, 0xFF, 0x03]).AsReceipt(),
            extraction: Purchase.ExtractionState.NeedsReview));

        _ledger.Given(purchase.Id, ExtractionStepResult.From(
            purchase.Id,
            "vision",
            "1.0",
            [ExtractionCandidate.Propose(1, "Bread", 2.50m), ExtractionCandidate.Propose(2, "Milk", amount: null)]));

        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject().ConfirmCandidates(purchase.Id));

        Assert.Equal(ApplicationErrors.ExtractionCandidateAmountRequired, error.Error.Code);
    }

    private static IExtractionStep Vision(params ExtractionCandidate[] candidates)
        => FakeStep.Producing("vision", ExtractionStepResult.From(0, "vision", "1.0", candidates)).ReadingImage();

    private ReceiptService Subject(params IExtractionStep[] steps)
        => new(_ledger, _ledger, _ledger, _ledger, _ledger, _ledger, _ledger, new ExtractionCascade(steps), _ledger);

    private static byte[] Jpeg(byte seed) => [0xFF, 0xD8, 0xFF, seed, 0x01, 0x02];
}
