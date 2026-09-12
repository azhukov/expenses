using Expenses.Application.Errors;
using Expenses.Application.Services;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain.Entities;

namespace Expenses.Application.Tests.ReferenceData;

/// <summary>
/// Scenarios from reference-data: "Reference entries are identified by a stable code",
/// "Categories are hierarchical", "Users can create their own categories", "Entries are retired,
/// not deleted", "Dictionaries can be listed", "Units describe what a quantity counts".
/// </summary>
public sealed class ReferenceDataTests
{
    private readonly InMemoryLedger _ledger = new();

    private CategoryService Categories => new(_ledger, _ledger);

    private UnitService Units => new(_ledger);

    [Fact]
    public async Task Create_a_category()
    {
        var created = await Categories.Create("GROCERIES", "Groceries");

        Assert.Equal("GROCERIES", created.Code);
        Assert.False(created.IsSystem);
        Assert.True(created.IsActive);
        Assert.Single(_ledger.Categories);
    }

    [Fact]
    public async Task Child_category()
    {
        _ledger.Given(Category.Create("GROCERIES", "Groceries"));

        var produce = await Categories.Create("PRODUCE", "Produce", "GROCERIES");

        Assert.Equal("GROCERIES", produce.ParentCode);
    }

    [Fact]
    public async Task Duplicate_code_rejected()
    {
        _ledger.Given(Category.Create("GROCERIES", "Groceries"));

        var error = await Assert.ThrowsAsync<ExpensesException>(() =>
            Categories.Create("GROCERIES", "Food and Drink"));

        Assert.Equal(ApplicationErrors.CategoryDuplicateCode, error.Error.Code);
        Assert.Single(_ledger.Categories);
    }

    [Fact]
    public async Task Renaming_does_not_break_references()
    {
        var groceries = _ledger.Given(Category.Create("GROCERIES", "Groceries", isSystem: true));

        var renamed = await Categories.Rename("GROCERIES", "Food and Drink");

        Assert.Equal("Food and Drink", renamed.Name);
        Assert.Equal("GROCERIES", renamed.Code);
        Assert.Equal(groceries.Id, renamed.Id);
    }

    [Fact]
    public async Task Code_cannot_be_changed()
    {
        _ledger.Given(Category.Create("GROCERIES", "Groceries"));

        var error = await Assert.ThrowsAsync<ExpensesException>(() =>
            Categories.Rename("GROCERIES", "Groceries", requestedCode: "FOOD"));

        Assert.Equal(ApplicationErrors.CategoryCodeImmutable, error.Error.Code);
        Assert.Equal("GROCERIES", _ledger.Categories[0].Code);
    }

    [Fact]
    public async Task System_category_cannot_be_deleted()
    {
        _ledger.Given(Category.Create("GROCERIES", "Groceries", isSystem: true));

        var error = await Assert.ThrowsAsync<ExpensesException>(() =>
            Categories.Delete("GROCERIES"));

        Assert.Equal(ApplicationErrors.CategorySystemUndeletable, error.Error.Code);
        Assert.Single(_ledger.Categories);
        Assert.Empty(_ledger.RemovedCategories);
    }

    [Fact]
    public async Task A_user_created_category_can_be_deleted()
    {
        _ledger.Given(Category.Create("HOBBY", "Hobby"));

        await Categories.Delete("HOBBY");

        Assert.Empty(_ledger.Categories);
        Assert.Single(_ledger.RemovedCategories);
    }

    [Fact]
    public async Task Deactivated_entry_is_hidden_from_selection()
    {
        _ledger.Given(Category.Create("COMMUTING", "Commuting"));
        _ledger.Given(Category.Create("GROCERIES", "Groceries"));

        await Categories.Deactivate("COMMUTING");
        var offered = await Categories.List();

        Assert.Equal(["GROCERIES"], offered.Select(category => category.Code));
    }

    [Fact]
    public async Task Parent_with_active_children()
    {
        var groceries = _ledger.Given(Category.Create("GROCERIES", "Groceries"));
        _ledger.Given(Category.Create("PRODUCE", "Produce", groceries));

        var error = await Assert.ThrowsAsync<ExpensesException>(() =>
            Categories.Deactivate("GROCERIES"));

        Assert.Equal(ApplicationErrors.CategoryHasActiveChildren, error.Error.Code);
        Assert.Equal("PRODUCE", Assert.IsType<IEnumerable<string>>(error.Error.Fields["children"], exactMatch: false).Single());
        Assert.True(_ledger.Categories.Single(category => category.Code == "GROCERIES").IsActive);
    }

    [Fact]
    public async Task Deactivating_a_category_whose_children_are_inactive_succeeds()
    {
        var groceries = _ledger.Given(Category.Create("GROCERIES", "Groceries"));
        var produce = _ledger.Given(Category.Create("PRODUCE", "Produce", groceries));
        produce.Deactivate();

        var deactivated = await Categories.Deactivate("GROCERIES");

        Assert.False(deactivated.IsActive);
    }

    [Fact]
    public async Task Deactivating_a_category_that_does_not_exist_is_reported()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() =>
            Categories.Deactivate("NOPE"));

        Assert.Equal(ApplicationErrors.CategoryNotFound, error.Error.Code);
    }

    [Fact]
    public async Task Including_inactive_entries()
    {
        _ledger.Given(Category.Create("GROCERIES", "Groceries"));
        var commuting = _ledger.Given(Category.Create("COMMUTING", "Commuting"));
        commuting.Deactivate();

        var listed = await Categories.List(includeInactive: true);

        Assert.Equal(2, listed.Count);
        Assert.Contains(listed, category => !category.IsActive);
    }

    [Fact]
    public async Task Display_order_is_stable()
    {
        _ledger.Given(Category.Create("TRANSPORT", "Transport"));
        _ledger.Given(Category.Create("GROCERIES", "Groceries"));
        _ledger.Given(Category.Create("HOUSEHOLD", "Household"));

        var first = await Categories.List();
        var second = await Categories.List();

        Assert.Equal(["GROCERIES", "HOUSEHOLD", "TRANSPORT"], first.Select(category => category.Code));
        Assert.Equal(first.Select(category => category.Code), second.Select(category => category.Code));
    }

    [Fact]
    public async Task Unit_detail()
    {
        _ledger.Given(Unit.Create("KG", "Kilogram", "kg", Unit.UnitKind.Mass));

        var listed = await Units.List();

        var kilogram = Assert.Single(listed);
        Assert.Equal("kg", kilogram.Symbol);
        Assert.Equal(Unit.UnitKind.Mass, kilogram.Kind);
    }

    [Fact]
    public async Task Units_default_to_active_entries_only()
    {
        _ledger.Given(Unit.Create("KG", "Kilogram", "kg", Unit.UnitKind.Mass));
        var retired = _ledger.Given(Unit.Create("BUNCH", "Bunch", "bund", Unit.UnitKind.Count));
        retired.Deactivate();

        var listed = await Units.List();
        var withInactive = await Units.List(includeInactive: true);

        Assert.Equal(["KG"], listed.Select(unit => unit.Code));
        Assert.Equal(["BUNCH", "KG"], withInactive.Select(unit => unit.Code));
    }
}
