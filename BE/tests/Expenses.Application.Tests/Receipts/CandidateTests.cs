using Expenses.Application.Errors;
using Expenses.Application.Purchases;
using Expenses.Application.Receipts;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain;
using Expenses.Domain.Extraction;

namespace Expenses.Application.Tests.Receipts;

/// <summary>
/// Scenarios from receipt-ingestion: "Extracted lines are candidates until confirmed",
/// "Candidates are transient and are not part of the ledger", "Extraction can be re-run",
/// "Verbatim receipt text is preserved".
/// </summary>
public sealed class CandidateTests
{
    private static readonly DateTime Occurred = new(2026, 8, 24, 12, 50, 8, DateTimeKind.Unspecified);

    private readonly InMemoryLedger _ledger = new();

    private ConfirmCandidates Confirm => new(_ledger, _ledger, _ledger, _ledger, _ledger);

    private GetExtractionCandidates Read => new(_ledger, _ledger);

    [Fact]
    public async Task Candidates_do_not_change_the_ledger()
    {
        var purchase = await GivenExtractedPurchase();

        var view = await Read.Execute(purchase.Id);

        Assert.Equal(2, view.Result?.Candidates.Count);
        var expense = Assert.Single(purchase.Expenses);
        Assert.Equal("Groceries", expense.Description);
    }

    [Fact]
    public async Task Placeholder_results_are_identified()
    {
        var purchase = await GivenExtractedPurchase();

        var view = await Read.Execute(purchase.Id);

        Assert.Equal("placeholder", view.Result?.EngineName);
        Assert.Equal("1.0", view.Result?.EngineVersion);
    }

    [Fact]
    public async Task Reading_candidates_for_a_receipt_that_was_never_extracted()
    {
        var purchase = GivenPurchase();
        await Attach(purchase);

        var view = await Read.Execute(purchase.Id);

        Assert.Null(view.Result);
        Assert.False(view.CandidatesHeld);
        Assert.Equal(Receipt.ExtractionState.Pending, view.Receipt.State);
    }

    /// <summary>
    /// Candidates live only in memory (D12), so this is what a restart looks like from above: the
    /// state the purchase recorded is still there, and the suggestion is not.
    /// </summary>
    [Fact]
    public async Task Requesting_candidates_that_are_no_longer_held()
    {
        var purchase = await GivenExtractedPurchase();
        purchase.Receipt!.TransitionTo(Receipt.ExtractionState.Extracting);
        purchase.Receipt!.TransitionTo(Receipt.ExtractionState.Extracted);
        await _ledger.Discard(purchase.Id);

        var view = await Read.Execute(purchase.Id);

        Assert.False(view.CandidatesHeld);
        Assert.Null(view.Result);

        // Absence, not failure — and reading never starts extraction.
        Assert.Equal(Receipt.ExtractionState.Extracted, view.Receipt.State);
        Assert.Empty(_ledger.Queued.Skip(1));
    }

    [Fact]
    public async Task Confirmed_lines_are_unaffected_by_losing_candidates()
    {
        var purchase = await GivenExtractedPurchase();
        await Confirm.Execute(purchase.Id);

        await _ledger.Discard(purchase.Id);

        Assert.Equal(["Sladoled", "Cokolada"], purchase.Expenses.Select(expense => expense.Description));
        Assert.Equal(8.48m, purchase.Expenses.Sum(expense => expense.Amount));
    }

    [Fact]
    public async Task Confirming_candidates()
    {
        var purchase = await GivenExtractedPurchase();

        var confirmed = await Confirm.Execute(purchase.Id);

        Assert.Equal(["Sladoled", "Cokolada"], confirmed.Expenses.Select(expense => expense.Description));
        Assert.Equal(8.48m, confirmed.Expenses.Sum(expense => expense.Amount));
        Assert.Equal(purchase.Id, confirmed.Id);

        // Confirmation consumes the candidates, in the same transaction (D16).
        Assert.Null(await _ledger.FindLatest(purchase.Id));
    }

    [Fact]
    public async Task Verbatim_text_survives_confirmation()
    {
        var kilogram = _ledger.Given(Unit.Create("KG", "Kilogram", "kg", Unit.UnitKind.Mass));
        var purchase = await GivenExtractedPurchase(unitId: kilogram.Id, unitRaw: "Bund");

        var confirmed = await Confirm.Execute(purchase.Id);

        Assert.Equal(kilogram.Id, confirmed.Expenses[0].UnitId);
        Assert.Equal("Bund", confirmed.Expenses[0].UnitRaw);

        // The candidate is gone; the expense is where the printed text now lives (D9, D12).
        Assert.Null(await _ledger.FindLatest(purchase.Id));
        Assert.Equal("Bund", purchase.Expenses[0].UnitRaw);
    }

    [Fact]
    public async Task Confirming_candidates_that_do_not_reconcile()
    {
        var purchase = await GivenExtractedPurchase(secondAmount: 4.99m);

        var error = await Assert.ThrowsAsync<ExpensesException>(() => Confirm.Execute(purchase.Id));

        Assert.Equal(ApplicationErrors.PurchaseReconciliationMismatch, error.Error.Code);
        Assert.Equal(8.48m, error.Error.Fields["amount"]);
        Assert.Equal(9.48m, error.Error.Fields["expensesTotal"]);
        Assert.Equal("Groceries", Assert.Single(purchase.Expenses).Description);
        Assert.NotNull(await _ledger.FindLatest(purchase.Id));
    }

    [Fact]
    public async Task Confirming_edited_candidates()
    {
        var purchase = await GivenExtractedPurchase(secondAmount: 4.99m);

        var confirmed = await Confirm.Execute(purchase.Id, [
            new ExpenseCommand("Sladoled", 4.49m, ListUnitPrice: 8.50m, DiscountAmount: 4.01m),
            new ExpenseCommand("Cokolada", 3.99m),
        ]);

        Assert.Equal(8.48m, confirmed.Expenses.Sum(expense => expense.Amount));
        Assert.Equal(4.01m, confirmed.TotalSaving);
    }

    /// <summary>
    /// Editing works when nothing is held, which is what makes the transience of candidates
    /// harmless: a review interrupted by a restart can still be finished by hand (D12).
    /// </summary>
    [Fact]
    public async Task Confirming_edited_lines_when_no_candidates_are_held()
    {
        var purchase = GivenPurchase();
        await Attach(purchase);

        var confirmed = await Confirm.Execute(purchase.Id, [new ExpenseCommand("Sladoled", 8.48m)]);

        Assert.Equal("Sladoled", Assert.Single(confirmed.Expenses).Description);
    }

    [Fact]
    public async Task Confirming_candidates_that_were_never_produced()
    {
        var purchase = GivenPurchase();
        await Attach(purchase);

        var error = await Assert.ThrowsAsync<ExpensesException>(() => Confirm.Execute(purchase.Id));

        Assert.Equal(ApplicationErrors.ExtractionCandidatesNotFound, error.Error.Code);
    }

    [Fact]
    public async Task Discarding_candidates()
    {
        var purchase = await GivenExtractedPurchase();

        await new DiscardCandidates(_ledger, _ledger).Execute(purchase.Id);

        Assert.Null(await _ledger.FindLatest(purchase.Id));
        Assert.NotNull(purchase.Receipt);
        Assert.Single(purchase.Expenses);
    }

    [Fact]
    public async Task Re_run_after_confirmation()
    {
        var purchase = await GivenExtractedPurchase();
        await Confirm.Execute(purchase.Id);

        var requeued = await new RequeueExtraction(_ledger, _ledger, _ledger).Execute(purchase.Id);

        Assert.Equal(Receipt.ExtractionState.Pending, requeued.State);
        Assert.Equal(["Sladoled", "Cokolada"], purchase.Expenses.Select(expense => expense.Description));
    }

    private Purchase GivenPurchase(decimal amount = 8.48m) =>
        _ledger.Given(Purchase.Record(Occurred, amount, [Expense.Record("Groceries", amount)]));

    private Task Attach(Purchase purchase) =>
        new AttachReceiptImage(_ledger, _ledger, _ledger, _ledger).Execute(purchase.Id, Jpeg(1));

    private async Task<Purchase> GivenExtractedPurchase(
        decimal secondAmount = 3.99m,
        long? unitId = null,
        string? unitRaw = null)
    {
        var purchase = GivenPurchase();
        await Attach(purchase);

        _ledger.Given(purchase.Id, ExtractionResult.From(
            purchase.Id,
            "placeholder",
            "1.0",
            [
                ExtractionCandidate.Propose(
                    1,
                    "Sladoled",
                    4.49m,
                    listUnitPrice: 8.50m,
                    discountAmount: 4.01m,
                    unitId: unitId,
                    unitRaw: unitRaw),
                ExtractionCandidate.Propose(2, "Cokolada", secondAmount),
            ],
            total: 4.49m + secondAmount));

        return purchase;
    }

    private static byte[] Jpeg(byte seed) => [0xFF, 0xD8, 0xFF, seed, 0x01, 0x02];
}
