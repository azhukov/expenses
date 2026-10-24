using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Application.Services;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain.Entities;
using Expenses.Domain.Extraction;

namespace Expenses.Application.Tests.Receipts;

/// <summary>
/// Scenarios from receipt-ingestion, "Extracted unit text is matched to a unit": "A fiscal invoice's
/// units are matched", "Vision-extracted units are matched", "A code match is preferred to an
/// alias", "Unknown text leaves the line unmatched", "A re-run uses the aliases as they now stand";
/// and from reference-data: "Unit codes match in normalised form" and "An alias of an inactive unit
/// does not match". Matching runs after every extraction path: capture, fiscal capture, re-run.
/// </summary>
public sealed class UnitMatchingTests
{
    private const string Payload =
        "https://mapr.tax.gov.me/ic/#/verify?iic=32AA324CFF5030271E16D59F7F8EF636"
        + "&tin=02365928&crtd=2026-08-29T14:59:22+02:00&prc=59.65";

    private static readonly DateTime s_occurred = new(2026, 8, 29, 14, 59, 22, DateTimeKind.Unspecified);

    private readonly InMemoryLedger _ledger = new();
    private readonly Unit _piece;
    private readonly Unit _pack;
    private readonly Unit _kilogram;

    public UnitMatchingTests()
    {
        _piece = _ledger.Given(Unit.Create("PCS", "Piece", "pcs", Unit.UnitKind.Count));
        _pack = _ledger.Given(Unit.Create("PACK", "Pack", "pack", Unit.UnitKind.Count));
        _kilogram = _ledger.Given(Unit.Create("KG", "Kilogram", "kg", Unit.UnitKind.Mass));
        _ledger.Given(UnitAlias.Create(_piece.Id, "kom"));
    }

    /// <summary>
    /// The MEGA MALL receipt (1000023219.jpg) as the fiscal portal returns it: fourteen lines, eight
    /// in KOM and six in KG.
    /// </summary>
    [Fact]
    public async Task A_fiscal_invoice_s_units_are_matched()
    {
        string[] printed = ["KOM", "KOM", "KOM", "KG", "KOM", "KOM", "KOM", "KG", "KG", "KG", "KG", "KOM", "KOM", "KG"];
        var fiscal = FakeStep.Producing("fiscal", Lines("fiscal", printed));

        var result = await Subject(fiscal).CaptureFiscal(Payload);

        var candidates = result.Result!.Candidates;
        Assert.Equal(14, candidates.Count);
        Assert.Equal(8, candidates.Count(line => line.UnitId == _piece.Id));
        Assert.Equal(6, candidates.Count(line => line.UnitId == _kilogram.Id));
        Assert.Equal(printed, candidates.Select(line => line.UnitRaw));
    }

    [Fact]
    public async Task Vision_extracted_units_are_matched()
    {
        var vision = FakeStep.Producing("vision", Lines("vision", "kom")).ReadingImage();

        var result = await Subject(vision).Capture(Jpeg(1));

        Assert.Equal(_piece.Id, Assert.Single(result.Result!.Candidates).UnitId);
    }

    [Fact]
    public async Task A_code_match_is_preferred_to_an_alias()
    {
        // Refused by the alias service; given directly, so the order of matching is what decides.
        _ledger.Given(UnitAlias.Create(_kilogram.Id, "pcs"));

        var result = await Subject(FakeStep.Producing("fiscal", Lines("fiscal", "PCS"))).CaptureFiscal(Payload);

        Assert.Equal(_piece.Id, Assert.Single(result.Result!.Candidates).UnitId);
    }

    [Fact]
    public async Task Unknown_text_leaves_the_line_unmatched()
    {
        var result = await Subject(FakeStep.Producing("fiscal", Lines("fiscal", "Pfand"))).CaptureFiscal(Payload);

        var line = Assert.Single(result.Result!.Candidates);
        Assert.Null(line.UnitId);
        Assert.Equal("Pfand", line.UnitRaw);
    }

    [Theory]
    [InlineData("kg.")]
    [InlineData(" KG ")]
    [InlineData("/kg/")]
    public async Task Unit_codes_match_in_normalised_form(string printed)
    {
        var result = await Subject(FakeStep.Producing("fiscal", Lines("fiscal", printed))).CaptureFiscal(Payload);

        Assert.Equal(_kilogram.Id, Assert.Single(result.Result!.Candidates).UnitId);
    }

    [Fact]
    public async Task An_alias_of_an_inactive_unit_does_not_match()
    {
        _ledger.Given(UnitAlias.Create(_pack.Id, "pak"));
        _pack.Deactivate();

        var result = await Subject(FakeStep.Producing("fiscal", Lines("fiscal", "pak", "PACK"))).CaptureFiscal(Payload);

        Assert.All(result.Result!.Candidates, line => Assert.Null(line.UnitId));
    }

    [Fact]
    public async Task A_re_run_uses_the_aliases_as_they_now_stand()
    {
        var purchase = GivenPurchaseWithReceipt();
        var vision = FakeStep.Producing("vision", Lines("vision", "KD")).ReadingImage();

        await Subject(vision).RerunExtraction(purchase.Id);
        Assert.Null(Assert.Single((await _ledger.FindLatest(purchase.Id))!.Candidates).UnitId);

        _ledger.Given(UnitAlias.Create(_piece.Id, "kd"));
        await Subject(FakeStep.Producing("vision", Lines("vision", "KD")).ReadingImage()).RerunExtraction(purchase.Id);

        Assert.Equal(_piece.Id, Assert.Single((await _ledger.FindLatest(purchase.Id))!.Candidates).UnitId);
    }

    [Fact]
    public async Task A_unit_a_stage_matched_itself_is_kept()
    {
        var own = ExtractionStepResult.From(
            0,
            "fiscal",
            "1.0",
            [ExtractionCandidate.Propose(1, "Line 1", 1.00m, unitRaw: "kom", unitId: _pack.Id)],
            total: 1.00m);

        var result = await Subject(FakeStep.Producing("fiscal", own)).CaptureFiscal(Payload);

        Assert.Equal(_pack.Id, Assert.Single(result.Result!.Candidates).UnitId);
    }

    private static ExtractionStepResult Lines(string stage, params string[] units)
        => ExtractionStepResult.From(
            0,
            stage,
            "1.0",
            units.Select((unit, index) => ExtractionCandidate.Propose(index + 1, $"Line {index + 1}", 1.00m, unitRaw: unit)),
            total: units.Length);

    private Purchase GivenPurchaseWithReceipt()
    {
        var stored = _ledger.GivenReceiptFile(Jpeg(1));

        return _ledger.Given(Purchase.Record(
            s_occurred,
            1.00m,
            [Expense.Record("Groceries", 1.00m)],
            receipt: stored.AsReceipt(),
            extraction: Purchase.ExtractionState.NeedsReview));
    }

    private ReceiptService Subject(params IExtractionStep[] steps)
        => new(_ledger, _ledger, _ledger, _ledger, _ledger, _ledger, _ledger, new ExtractionCascade(steps), _ledger);

    private static byte[] Jpeg(byte seed) => [0xFF, 0xD8, 0xFF, seed, 0x01, 0x02];
}
