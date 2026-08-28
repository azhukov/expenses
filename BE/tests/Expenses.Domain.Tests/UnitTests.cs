namespace Expenses.Domain.Tests;

/// <summary>Scenarios from reference-data: "Units describe what a quantity counts".</summary>
public sealed class UnitTests
{
    [Fact]
    public void Unit_detail()
    {
        var kilogram = Unit.Create("KG", "Kilogram", "kg", Unit.UnitKind.Mass);

        Assert.Equal("KG", kilogram.Code);
        Assert.Equal("kg", kilogram.Symbol);
        Assert.Equal(Unit.UnitKind.Mass, kilogram.Kind);
        Assert.True(kilogram.IsActive);
    }

    [Fact]
    public void Code_is_required()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            Unit.Create(" ", "Kilogram", "kg", Unit.UnitKind.Mass));

        Assert.Equal("code", error.ParamName);
    }

    [Fact]
    public void Name_is_required()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            Unit.Create("KG", " ", "kg", Unit.UnitKind.Mass));

        Assert.Equal("name", error.ParamName);
    }

    [Fact]
    public void Deactivated_unit_is_marked_inactive()
    {
        var unit = Unit.Create("BUND", "Bunch", "bund", Unit.UnitKind.Count);

        unit.Deactivate();

        Assert.False(unit.IsActive);
    }
}
