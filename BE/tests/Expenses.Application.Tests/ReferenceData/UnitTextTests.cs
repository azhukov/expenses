using Expenses.Application.Services;

namespace Expenses.Application.Tests.ReferenceData;

/// <summary>
/// Scenarios from reference-data: "Aliases are compared in normalised form" — "Spellings that
/// differ only in form are one alias", "Inner characters are significant", "Text that normalises
/// to nothing".
/// </summary>
public sealed class UnitTextTests
{
    [Theory]
    [InlineData("KOM")]
    [InlineData(" kom ")]
    [InlineData("kom.")]
    [InlineData("/kom/")]
    [InlineData(" /KOM/. ")]
    public void Spellings_that_differ_only_in_form_are_one_alias(string printed)
        => Assert.Equal("kom", UnitText.Normalise(printed));

    [Fact]
    public void Internal_whitespace_collapses_to_one_space()
        => Assert.Equal("fl oz", UnitText.Normalise("FL \t  OZ"));

    [Theory]
    [InlineData("100kom", "100kom")]
    [InlineData("t/k", "t/k")]
    [InlineData("kg.m", "kg.m")]
    public void Inner_characters_are_significant(string printed, string normalised)
        => Assert.Equal(normalised, UnitText.Normalise(printed));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" ./ ")]
    [InlineData("./.")]
    public void Text_that_normalises_to_nothing(string? printed)
        => Assert.Null(UnitText.Normalise(printed));

    [Fact]
    public void Non_latin_text_is_lowercased_and_kept()
        => Assert.Equal("шт", UnitText.Normalise("ШТ."));
}
