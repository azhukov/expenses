using Expenses.Application.Dtos;
using Expenses.Application.Errors;
using Expenses.Application.Services;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain.Entities;
using Expenses.Domain.Extraction;

namespace Expenses.Application.Tests.Purchases;

/// <summary>
/// Scenarios from purchase-recording, "Expense line detail": "Printed unit text alone satisfies the
/// rule", "A supplied unit code overrides the printed text", "An expense without a unit is
/// rejected"; and "Saving an expense line teaches a unit alias": "An unknown spelling is learned",
/// "Learning applies to direct recording", "A known alias is not overridden", "A matched unit
/// teaches nothing", "A code teaches nothing", "The earlier line decides within one save", "A
/// rejected save teaches nothing", "History is not re-matched". From receipt-ingestion: "A receipt
/// whose units all match can be confirmed unchanged".
/// </summary>
public sealed class UnitLearningTests
{
    private static readonly DateTime s_occurred = new(2026, 8, 29, 14, 59, 22, DateTimeKind.Unspecified);

    private readonly InMemoryLedger _ledger = new();
    private readonly Unit _piece;
    private readonly Unit _pack;
    private readonly Unit _kilogram;

    public UnitLearningTests()
    {
        _piece = _ledger.Given(Unit.Create("PCS", "Piece", "pcs", Unit.UnitKind.Count));
        _pack = _ledger.Given(Unit.Create("PACK", "Pack", "pack", Unit.UnitKind.Count));
        _kilogram = _ledger.Given(Unit.Create("KG", "Kilogram", "kg", Unit.UnitKind.Mass));
        _ledger.Given(UnitAlias.Create(_piece.Id, "kom"));
    }

    private PurchaseService Purchases
        => new(_ledger, _ledger, _ledger, _ledger, new MerchantService(_ledger, _ledger), _ledger, _ledger, _ledger);

    private ReceiptService Receipts
        => new(_ledger, _ledger, _ledger, _ledger, _ledger, _ledger, _ledger, new ExtractionCascade([]), _ledger);

    // ---- Resolving a line's unit from its printed text ---------------------

    [Fact]
    public async Task Printed_unit_text_alone_satisfies_the_rule()
    {
        var result = await Purchases.Record(s_occurred, 2.50m, [new ExpenseCommand("JAJA", 2.50m, UnitRaw: "KOM")]);

        var expense = Assert.Single(result.Purchase.Expenses);
        Assert.Equal(_piece.Id, expense.UnitId);
        Assert.Equal("KOM", expense.UnitRaw);
    }

    [Fact]
    public async Task A_supplied_unit_code_overrides_the_printed_text()
    {
        var result = await Purchases.Record(
            s_occurred,
            2.50m,
            [new ExpenseCommand("JAJA", 2.50m, UnitCode: "PACK", UnitRaw: "kom")]);

        Assert.Equal(_pack.Id, Assert.Single(result.Purchase.Expenses).UnitId);
    }

    [Fact]
    public async Task An_expense_whose_printed_unit_matches_nothing_is_rejected()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Purchases.Record(
            s_occurred,
            0.10m,
            [new ExpenseCommand("Deposit", 0.10m, UnitRaw: "Pfand")]));

        Assert.Equal(ApplicationErrors.ExpenseUnitRequired, error.Error.Code);
        Assert.Empty(_ledger.Purchases);
        Assert.DoesNotContain(_ledger.UnitAliases, alias => alias.Alias == "pfand");
    }

    // ---- Learning ------------------------------------------------------------

    [Fact]
    public async Task An_unknown_spelling_is_learned()
    {
        var tempKey = _ledger.GivenTemporaryCapture([0xFF, 0xD8, 0xFF, 0x01]);

        var result = await Purchases.Record(
            s_occurred,
            2.50m,
            [new ExpenseCommand("JAJA", 2.50m, UnitCode: "PCS", UnitRaw: "KD")],
            capture: new CapturedReceiptCommand(tempKey, Purchase.ExtractionState.Extracted));

        var expense = Assert.Single(result.Purchase.Expenses);
        Assert.Equal(_piece.Id, expense.UnitId);
        Assert.Equal("KD", expense.UnitRaw);
        Assert.Equal(_piece.Id, AliasOf("kd"));
    }

    [Fact]
    public async Task Learning_applies_to_direct_recording()
    {
        await Purchases.Record(s_occurred, 3.00m, [new ExpenseCommand("Crackers", 3.00m, UnitCode: "PACK", UnitRaw: "kut")]);

        Assert.Equal(_pack.Id, AliasOf("kut"));
    }

    [Fact]
    public async Task Learning_applies_to_confirming_candidates()
    {
        var purchase = GivenExtractedPurchase(ExtractionCandidate.Propose(1, "JAJA", 2.50m, unitRaw: "KD"));

        await Receipts.ConfirmCandidates(purchase.Id, [new ExpenseCommand("JAJA", 2.50m, UnitCode: "PCS", UnitRaw: "KD")]);

        Assert.Equal(_piece.Id, AliasOf("kd"));
    }

    [Fact]
    public async Task A_known_alias_is_not_overridden()
    {
        var result = await Purchases.Record(
            s_occurred,
            2.50m,
            [new ExpenseCommand("JAJA", 2.50m, UnitCode: "PACK", UnitRaw: "KOM")]);

        Assert.Equal(_pack.Id, Assert.Single(result.Purchase.Expenses).UnitId);
        Assert.Equal(_piece.Id, AliasOf("kom"));
        Assert.Single(_ledger.UnitAliases);
    }

    [Fact]
    public async Task A_matched_unit_teaches_nothing()
    {
        var purchase = GivenExtractedPurchase(ExtractionCandidate.Propose(1, "JAJA", 2.50m, unitRaw: "KOM", unitId: _piece.Id));

        await Receipts.ConfirmCandidates(purchase.Id);

        Assert.Equal(["kom"], _ledger.UnitAliases.Select(alias => alias.Alias));
    }

    [Fact]
    public async Task A_code_teaches_nothing()
    {
        await Purchases.Record(s_occurred, 2.50m, [new ExpenseCommand("JAJA", 2.50m, UnitCode: "PCS", UnitRaw: "kg.")]);

        Assert.DoesNotContain(_ledger.UnitAliases, alias => alias.Alias == "kg");
    }

    [Fact]
    public async Task The_earlier_line_decides_within_one_save()
    {
        var result = await Purchases.Record(
            s_occurred,
            2.00m,
            [
                new ExpenseCommand("First", 1.00m, UnitCode: "PCS", UnitRaw: "zz"),
                new ExpenseCommand("Second", 1.00m, UnitCode: "PACK", UnitRaw: "zz"),
            ]);

        Assert.Equal(_piece.Id, AliasOf("zz"));
        Assert.Equal(_pack.Id, result.Purchase.Expenses[1].UnitId);
    }

    [Fact]
    public async Task A_rejected_save_teaches_nothing()
    {
        await Assert.ThrowsAsync<ExpensesException>(() => Purchases.Record(
            s_occurred,
            9.99m,
            [new ExpenseCommand("JAJA", 2.50m, UnitCode: "PCS", UnitRaw: "KD")]));

        Assert.Null(AliasOf("kd"));
    }

    [Fact]
    public async Task History_is_not_re_matched()
    {
        var earlier = _ledger.Given(Purchase.Record(
            s_occurred.AddDays(-1),
            2.50m,
            [Expense.Record("JAJA", 2.50m, unitRaw: "KD")]));

        await Purchases.Record(s_occurred, 2.50m, [new ExpenseCommand("JAJA", 2.50m, UnitCode: "PCS", UnitRaw: "KD")]);

        Assert.Equal(_piece.Id, AliasOf("kd"));
        Assert.Null(Assert.Single(earlier.Expenses).UnitId);
    }

    [Fact]
    public async Task A_receipt_whose_units_all_match_can_be_confirmed_unchanged()
    {
        var purchase = _ledger.Given(Purchase.Record(
            s_occurred,
            2.50m,
            [Expense.Record("Groceries", 2.50m)],
            receipt: _ledger.GivenReceiptFile([0xFF, 0xD8, 0xFF, 0x02]).AsReceipt(),
            extraction: Purchase.ExtractionState.NeedsReview));
        var vision = FakeStep.Producing(
            "vision",
            ExtractionStepResult.From(
                purchase.Id,
                "vision",
                "1.0",
                [
                    ExtractionCandidate.Propose(1, "JAJA", 1.00m, unitRaw: "KOM"),
                    ExtractionCandidate.Propose(2, "PARADAJZ", 1.50m, unitRaw: "KG"),
                ],
                total: 2.50m)).ReadingImage();

        await new ReceiptService(_ledger, _ledger, _ledger, _ledger, _ledger, _ledger, _ledger, new ExtractionCascade([vision]), _ledger)
            .RerunExtraction(purchase.Id);
        var confirmed = await Receipts.ConfirmCandidates(purchase.Id);

        Assert.Equal(["PCS", "KG"], confirmed.Expenses.Select(expense => CodeOf(expense.UnitId)));
    }

    private Purchase GivenExtractedPurchase(ExtractionCandidate candidate)
    {
        var purchase = _ledger.Given(Purchase.Record(
            s_occurred,
            candidate.Amount,
            [Expense.Record("Groceries", candidate.Amount)],
            receipt: _ledger.GivenReceiptFile([0xFF, 0xD8, 0xFF, 0x03]).AsReceipt(),
            extraction: Purchase.ExtractionState.Extracted));

        _ledger.Given(purchase.Id, ExtractionStepResult.From(purchase.Id, "vision", "1.0", [candidate], total: candidate.Amount));

        return purchase;
    }

    private long? AliasOf(string alias) => _ledger.UnitAliases.SingleOrDefault(existing => existing.Alias == alias)?.UnitId;

    private string? CodeOf(long? unitId)
        => new[] { _piece, _pack, _kilogram }.FirstOrDefault(unit => unit.Id == unitId)?.Code;
}
