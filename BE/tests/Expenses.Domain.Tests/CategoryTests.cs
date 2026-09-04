namespace Expenses.Domain.Tests;

/// <summary>
/// Scenarios from reference-data: "Reference entries are identified by a stable code",
/// "Categories are hierarchical", "Entries are retired, not deleted".
/// </summary>
public sealed class CategoryTests
{
    [Fact]
    public void Child_category()
    {
        var groceries = Category.Create("GROCERIES", "Groceries");

        var produce = Category.Create("PRODUCE", "Produce", groceries);

        Assert.Same(groceries, produce.Parent);
        Assert.Contains(produce, groceries.Children);
    }

    [Fact]
    public void Renaming_does_not_change_the_code()
    {
        var groceries = Category.Create("GROCERIES", "Groceries");

        groceries.Rename("Food and Drink");

        Assert.Equal("GROCERIES", groceries.Code);
        Assert.Equal("Food and Drink", groceries.Name);
    }

    [Fact]
    public void Cycle_rejected_when_a_category_is_made_its_own_parent()
    {
        var groceries = Category.Create("GROCERIES", "Groceries");

        var error = Assert.Throws<InvalidOperationException>(() => groceries.SetParent(groceries));

        Assert.Contains("cycle", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cycle_rejected_when_a_category_is_made_a_descendant_of_itself()
    {
        var groceries = Category.Create("GROCERIES", "Groceries");
        var produce = Category.Create("PRODUCE", "Produce", groceries);
        var citrus = Category.Create("CITRUS", "Citrus", produce);

        var error = Assert.Throws<InvalidOperationException>(() => groceries.SetParent(citrus));

        Assert.Contains("cycle", error.Message, StringComparison.Ordinal);
        Assert.Null(groceries.Parent);
    }

    [Fact]
    public void Depth_is_not_limited()
    {
        var current = Category.Create("L0", "Level 0");
        for (int level = 1; level <= 12; level++)
        {
            current = Category.Create($"L{level}", $"Level {level}", current);
        }

        Assert.NotNull(current.Parent);
    }

    [Fact]
    public void Code_is_required()
    {
        var error = Assert.Throws<ArgumentException>(() => Category.Create("  ", "Groceries"));

        Assert.Equal("code", error.ParamName);
    }

    [Fact]
    public void Name_is_required()
    {
        var error = Assert.Throws<ArgumentException>(() => Category.Create("GROCERIES", " "));

        Assert.Equal("name", error.ParamName);
    }

    [Fact]
    public void A_new_category_is_active_and_not_system_provided()
    {
        var category = Category.Create("COMMUTING", "Commuting");

        Assert.True(category.IsActive);
        Assert.False(category.IsSystem);
    }

    [Fact]
    public void Seeded_entries_are_system_provided()
    {
        var category = Category.Create("GROCERIES", "Groceries", isSystem: true);

        Assert.True(category.IsSystem);
    }

    [Fact]
    public void Deactivated_entry_is_marked_inactive()
    {
        var category = Category.Create("COMMUTING", "Commuting");

        category.Deactivate();

        Assert.False(category.IsActive);
    }

    [Fact]
    public void Setting_a_parent_to_null_detaches_the_category()
    {
        var groceries = Category.Create("GROCERIES", "Groceries");
        var produce = Category.Create("PRODUCE", "Produce", groceries);

        produce.SetParent(null);

        Assert.Null(produce.Parent);
        Assert.DoesNotContain(produce, groceries.Children);
    }
}
