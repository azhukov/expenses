using Expenses.Application.Errors;
using Expenses.Application.Services;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain.Entities;

namespace Expenses.Application.Tests.ReferenceData;

/// <summary>
/// Scenarios from reference-data: "Unit aliases can be added and removed" — adding, adding one the
/// unit already has, to an unknown unit, to a deactivated unit, removing, removing leaves history
/// unchanged, removing from the wrong unit; "Unit aliases map printed text to a unit" — one alias one
/// unit, an alias may not repeat a unit code; "Aliases are compared in normalised form" — the alias
/// is stored normalised, text that normalises to nothing; "Dictionaries can be listed" — units are
/// listed with their aliases; "Units describe what a quantity counts" — adding an alias does not
/// change the unit.
/// </summary>
public sealed class UnitAliasManagementTests
{
    private readonly InMemoryLedger _ledger = new();
    private readonly Unit _piece;
    private readonly Unit _pack;

    public UnitAliasManagementTests()
    {
        _piece = _ledger.Given(Unit.Create("PCS", "Piece", "pcs", Unit.UnitKind.Count));
        _pack = _ledger.Given(Unit.Create("PACK", "Pack", "pack", Unit.UnitKind.Count));
        _ledger.Given(Unit.Create("KG", "Kilogram", "kg", Unit.UnitKind.Mass));
        _ledger.Given(UnitAlias.Create(_piece.Id, "kom"));
    }

    private UnitService Units => new(_ledger, _ledger, _ledger);

    [Fact]
    public async Task Units_are_listed_with_their_aliases()
    {
        _ledger.Given(UnitAlias.Create(_piece.Id, "kd"));

        var listed = await Units.List();

        Assert.Equal(["kd", "kom"], listed.Single(unit => unit.Code == "PCS").Aliases);
        Assert.Empty(listed.Single(unit => unit.Code == "KG").Aliases);
    }

    [Fact]
    public async Task Adding_an_alias()
    {
        var view = await Units.AddAlias("PACK", "kut");

        Assert.Equal("PACK", view.Code);
        Assert.Contains("kut", view.Aliases);
        Assert.Equal(_pack.Id, AliasOf("kut"));
    }

    [Fact]
    public async Task Adding_an_alias_the_unit_already_has()
    {
        var view = await Units.AddAlias("PCS", "KOM");

        Assert.Equal(["kom"], view.Aliases);
        Assert.Single(_ledger.UnitAliases, alias => alias.Alias == "kom");
    }

    [Fact]
    public async Task The_alias_is_stored_normalised()
    {
        var view = await Units.AddAlias("PCS", "  KD. ");

        Assert.Contains("kd", view.Aliases);
        Assert.Equal(_piece.Id, AliasOf("kd"));
    }

    [Fact]
    public async Task The_unit_code_is_matched_regardless_of_case()
        => Assert.Equal("PACK", (await Units.AddAlias("pack", "kut")).Code);

    [Fact]
    public async Task Adding_an_alias_to_an_unknown_unit()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Units.AddAlias("NOPE", "kut"));

        Assert.Equal(ApplicationErrors.UnitNotFound, error.Error.Code);
        Assert.Null(AliasOf("kut"));
    }

    [Fact]
    public async Task Adding_an_alias_to_a_deactivated_unit()
    {
        _pack.Deactivate();

        var error = await Assert.ThrowsAsync<ExpensesException>(() => Units.AddAlias("PACK", "kut"));

        Assert.Equal(ApplicationErrors.UnitInactive, error.Error.Code);
    }

    [Fact]
    public async Task An_alias_may_not_repeat_a_unit_code()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Units.AddAlias("PCS", "kg"));

        Assert.Equal(ApplicationErrors.UnitAliasIsUnitCode, error.Error.Code);
        Assert.Equal("KG", error.Error.Fields["unitCode"]);
    }

    [Fact]
    public async Task One_alias_one_unit()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Units.AddAlias("PACK", "kom"));

        Assert.Equal(ApplicationErrors.UnitAliasConflict, error.Error.Code);
        Assert.Equal("kom", error.Error.Fields["alias"]);
        Assert.Equal("PCS", error.Error.Fields["unitCode"]);
        Assert.Contains("already belongs to Piece", error.Error.Message, StringComparison.Ordinal);
        Assert.Equal(_piece.Id, AliasOf("kom"));
    }

    [Fact]
    public async Task Text_that_normalises_to_nothing()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Units.AddAlias("PCS", " ./ "));

        Assert.Equal(ApplicationErrors.UnitAliasEmpty, error.Error.Code);
    }

    [Fact]
    public async Task Adding_an_alias_does_not_change_the_unit()
    {
        var view = await Units.AddAlias("PCS", "kd");

        Assert.Equal(("PCS", "Piece", "pcs", Unit.UnitKind.Count), (view.Code, view.Name, view.Symbol, view.Kind));
    }

    [Fact]
    public async Task Removing_an_alias()
    {
        await Units.RemoveAlias("PCS", "KOM.");

        Assert.Null(AliasOf("kom"));
        Assert.Empty((await Units.List()).Single(unit => unit.Code == "PCS").Aliases);
    }

    [Fact]
    public async Task Removing_an_alias_from_the_wrong_unit()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Units.RemoveAlias("PACK", "kom"));

        Assert.Equal(ApplicationErrors.UnitAliasNotFound, error.Error.Code);
        Assert.Equal(_piece.Id, AliasOf("kom"));
    }

    [Fact]
    public async Task Removing_an_alias_that_does_not_exist()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Units.RemoveAlias("PCS", "zz"));

        Assert.Equal(ApplicationErrors.UnitAliasNotFound, error.Error.Code);
    }

    [Fact]
    public async Task Removing_an_alias_leaves_history_unchanged()
    {
        var purchase = _ledger.Given(Purchase.Record(
            new DateTime(2026, 8, 29, 0, 0, 0, DateTimeKind.Unspecified),
            2.50m,
            [Expense.Record("JAJA", 2.50m, unitId: _piece.Id, unitRaw: "kom")]));

        await Units.RemoveAlias("PCS", "kom");

        var expense = Assert.Single(purchase.Expenses);
        Assert.Equal(_piece.Id, expense.UnitId);
        Assert.Equal("kom", expense.UnitRaw);
    }

    private long? AliasOf(string alias) => _ledger.UnitAliases.SingleOrDefault(existing => existing.Alias == alias)?.UnitId;
}
