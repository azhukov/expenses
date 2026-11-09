using Expenses.Application.Errors;
using Expenses.Application.Services;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain.Entities;

namespace Expenses.Application.Tests.Purchases;

/// <summary>
/// Scenarios from ledger-export: "The ledger can be exported for a chosen range", "An export has
/// one row per expense line", "An export names references rather than identifying them" and "An
/// export is ordered as a spreadsheet reads". Encoding is covered by <c>LedgerCsvTests</c>.
/// </summary>
public sealed class ExportLedgerTests
{
    private readonly InMemoryLedger _ledger = new();

    private PurchaseService Subject
        => new(_ledger, _ledger, _ledger, _ledger, new MerchantService(_ledger, _ledger), _ledger, _ledger, _ledger, _ledger);

    // ---- The ledger can be exported for a chosen range ---------------------

    [Fact]
    public async Task Export_everything()
    {
        GivenPurchaseOn(At(2026, 8, 10), 1.00m);
        GivenPurchaseOn(At(2026, 9, 10), 2.00m);
        GivenPurchaseOn(At(2026, 10, 10), 3.00m);

        var lines = await Subject.Export();

        Assert.Equal([1.00m, 2.00m, 3.00m], lines.Select(line => line.Amount));
    }

    [Fact]
    public async Task Export_one_month()
    {
        GivenPurchaseOn(At(2026, 9, 30, 23, 59), 1.00m);
        GivenPurchaseOn(At(2026, 10, 1), 2.00m);
        GivenPurchaseOn(At(2026, 10, 31, 23, 59), 3.00m);
        GivenPurchaseOn(At(2026, 11, 1), 4.00m);

        var lines = await Subject.Export(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        Assert.Equal([2.00m, 3.00m], lines.Select(line => line.Amount));
    }

    [Fact]
    public async Task Only_a_start_is_given()
    {
        GivenPurchaseOn(At(2026, 10, 14, 23, 59), 1.00m);
        GivenPurchaseOn(At(2026, 10, 15), 2.00m);
        GivenPurchaseOn(At(2026, 12, 1), 3.00m);

        var lines = await Subject.Export(from: new DateOnly(2026, 10, 15));

        Assert.Equal([2.00m, 3.00m], lines.Select(line => line.Amount));
    }

    [Fact]
    public async Task Only_an_end_is_given()
    {
        GivenPurchaseOn(At(2026, 1, 1), 1.00m);
        GivenPurchaseOn(At(2026, 10, 15, 23, 59), 2.00m);
        GivenPurchaseOn(At(2026, 10, 16), 3.00m);

        var lines = await Subject.Export(to: new DateOnly(2026, 10, 15));

        Assert.Equal([1.00m, 2.00m], lines.Select(line => line.Amount));
    }

    [Fact]
    public async Task Nothing_in_the_range()
    {
        GivenPurchaseOn(At(2026, 10, 10), 1.00m);

        var lines = await Subject.Export(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        Assert.Empty(lines);
    }

    [Fact]
    public async Task An_export_is_not_paged()
    {
        int count = PurchaseService.MaxPageSize + 25;
        var start = At(2026, 1, 1);

        for (int i = 0; i < count; i++)
        {
            GivenPurchaseOn(start.AddHours(i), 1.00m + i);
        }

        var lines = await Subject.Export();

        Assert.Equal(count, lines.Count);
    }

    [Fact]
    public async Task An_inverted_range_is_rejected()
    {
        var from = new DateOnly(2026, 10, 31);
        var to = new DateOnly(2026, 10, 1);

        var exported = await Assert.ThrowsAsync<ExpensesException>(() => Subject.Export(from, to));
        var listed = await Assert.ThrowsAsync<ExpensesException>(() => Subject.List(from, to));

        Assert.Equal(ApplicationErrors.ListingRangeInvalid, exported.Error.Code);
        Assert.Equal(from, exported.Error.Fields["from"]);
        Assert.Equal(to, exported.Error.Fields["to"]);
        Assert.Equal(listed.Error.Message, exported.Error.Message);
    }

    [Fact]
    public async Task Exporting_changes_nothing()
    {
        GivenPurchaseOn(At(2026, 10, 10), 1.00m);

        await Subject.Export();

        Assert.Equal(0, _ledger.SaveCount);
        Assert.Single(_ledger.Purchases);
    }

    // ---- An export has one row per expense line ----------------------------

    [Fact]
    public async Task A_purchase_with_three_lines()
    {
        var purchase = _ledger.Given(Purchase.Record(
            At(2026, 10, 14, 13, 5),
            12.40m,
            [Expense.Record("Bread", 1.20m), Expense.Record("Milk", 3.20m), Expense.Record("Coffee", 8.00m)]));

        var lines = await Subject.Export();

        Assert.Equal(3, lines.Count);
        Assert.All(lines, line => Assert.Equal(purchase.Id, line.PurchaseId));
        Assert.All(lines, line => Assert.Equal(At(2026, 10, 14, 13, 5), line.OccurredAt));
        Assert.Equal(12.40m, lines.Sum(line => line.Amount));
    }

    [Fact]
    public async Task Line_detail_is_carried_into_its_row()
    {
        _ledger.Given(Purchase.Record(
            At(2026, 10, 14),
            4.00m,
            [Expense.Record("Apples", 4.00m, quantity: 1.25m, unitPrice: 3.20m, listUnitPrice: 3.60m, discountAmount: 0.50m)]));

        var line = Assert.Single(await Subject.Export());

        Assert.Equal("Apples", line.Description);
        Assert.Equal(1.25m, line.Quantity);
        Assert.Equal(3.20m, line.UnitPrice);
        Assert.Equal(3.60m, line.ListUnitPrice);
        Assert.Equal(0.50m, line.DiscountAmount);
        Assert.Equal(4.00m, line.Amount);
    }

    [Fact]
    public async Task Optional_values_that_are_absent()
    {
        GivenPurchaseOn(At(2026, 10, 14), 2.00m);

        var line = Assert.Single(await Subject.Export());

        Assert.Null(line.UnitPrice);
        Assert.Null(line.ListUnitPrice);
        Assert.Null(line.DiscountAmount);
        Assert.Null(line.Category);
        Assert.Null(line.Unit);
        Assert.Null(line.Merchant);
    }

    // ---- An export names references rather than identifying them -----------

    [Fact]
    public async Task Matched_references_are_named()
    {
        var voli = _ledger.Given(Merchant.Create("Voli"));
        var groceries = _ledger.Given(Category.Create("GROCERIES", "Groceries"));
        var kilogram = _ledger.Given(Unit.Create("KG", "Kilogram", "kg", Unit.UnitKind.Mass));

        _ledger.Given(Purchase.Record(
            At(2026, 10, 14),
            4.00m,
            [Expense.Record("Apples", 4.00m, unitId: kilogram.Id, categoryId: groceries.Id, categoryRaw: "VOCE", unitRaw: "KGM")],
            merchantId: voli.Id,
            merchantRaw: "VOLI TRADE DOO"));

        var line = Assert.Single(await Subject.Export());

        Assert.Equal("Voli", line.Merchant);
        Assert.Equal("Groceries", line.Category);
        Assert.Equal("kg", line.Unit);
    }

    [Fact]
    public async Task Unmatched_references_fall_back_to_what_was_printed()
    {
        _ledger.Given(Purchase.Record(
            At(2026, 10, 14),
            4.00m,
            [Expense.Record("Apples", 4.00m, categoryRaw: "VOCE", unitRaw: "KGM")],
            merchantRaw: "VOLI TRADE DOO"));

        var line = Assert.Single(await Subject.Export());

        Assert.Equal("VOLI TRADE DOO", line.Merchant);
        Assert.Equal("VOCE", line.Category);
        Assert.Equal("KGM", line.Unit);
    }

    [Fact]
    public async Task A_deactivated_merchant_is_still_named()
    {
        var merchant = _ledger.Given(Merchant.Create("Idea"));
        var category = _ledger.Given(Category.Create("OLD", "Old category"));
        var unit = _ledger.Given(Unit.Create("PCK", "Pack", "pack", Unit.UnitKind.Count));
        merchant.Deactivate();
        category.Deactivate();
        unit.Deactivate();

        _ledger.Given(Purchase.Record(
            At(2026, 10, 14),
            1.00m,
            [Expense.Record("Thing", 1.00m, unitId: unit.Id, categoryId: category.Id)],
            merchantId: merchant.Id));

        var line = Assert.Single(await Subject.Export());

        Assert.Equal("Idea", line.Merchant);
        Assert.Equal("Old category", line.Category);
        Assert.Equal("pack", line.Unit);
    }

    [Fact]
    public async Task Neither_a_reference_nor_verbatim_text()
    {
        GivenPurchaseOn(At(2026, 10, 14), 1.00m);

        var line = Assert.Single(await Subject.Export());

        Assert.Null(line.Category);
        Assert.Null(line.Merchant);
    }

    // ---- An export is ordered as a spreadsheet reads -----------------------

    [Fact]
    public async Task Oldest_first()
    {
        GivenPurchaseOn(At(2026, 10, 3), 3.00m);
        GivenPurchaseOn(At(2026, 10, 1), 1.00m);
        GivenPurchaseOn(At(2026, 10, 2), 2.00m);

        var lines = await Subject.Export();

        Assert.Equal([1.00m, 2.00m, 3.00m], lines.Select(line => line.Amount));
    }

    [Fact]
    public async Task Purchases_at_the_same_moment_keep_the_order_they_were_recorded_in()
    {
        var first = GivenPurchaseOn(At(2026, 10, 1, 12, 0), 5.00m);
        var second = GivenPurchaseOn(At(2026, 10, 1, 12, 0), 1.00m);

        var lines = await Subject.Export();

        Assert.Equal([first.Id, second.Id], lines.Select(line => line.PurchaseId));
    }

    [Fact]
    public async Task Lines_keep_their_recorded_order()
    {
        GivenPurchaseOn(At(2026, 10, 2), 9.00m);
        _ledger.Given(Purchase.Record(
            At(2026, 10, 1),
            6.00m,
            [Expense.Record("Bread", 1.00m), Expense.Record("Milk", 2.00m), Expense.Record("Eggs", 3.00m)]));

        var lines = await Subject.Export();

        Assert.Equal(["Bread", "Milk", "Eggs", "Line"], lines.Select(line => line.Description));
    }

    private static DateTime At(int year, int month, int day, int hour = 12, int minute = 0)
        => new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    private Purchase GivenPurchaseOn(DateTime occurredAt, decimal amount)
        => _ledger.Given(Purchase.Record(occurredAt, amount, [Expense.Record("Line", amount)]));
}
