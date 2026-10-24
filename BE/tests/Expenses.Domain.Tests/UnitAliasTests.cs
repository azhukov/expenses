using Expenses.Domain.Entities;

namespace Expenses.Domain.Tests;

/// <summary>Scenarios from reference-data: "Unit aliases map printed text to a unit".</summary>
public sealed class UnitAliasTests
{
    [Fact]
    public void An_alias_belongs_to_one_unit()
    {
        var alias = UnitAlias.Create(1, "kom");

        Assert.Equal(1, alias.UnitId);
        Assert.Equal("kom", alias.Alias);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_alias_is_refused(string text)
    {
        var error = Assert.Throws<ArgumentException>(() => UnitAlias.Create(1, text));

        Assert.Equal("alias", error.ParamName);
    }
}
