using System.Globalization;
using Expenses.Application.Services;

namespace Expenses.Application.Tests.Extraction;

/// <summary>
/// Money stated more precisely than the ledger keeps it, brought to the cent (D7): half away from
/// zero, as a till prints it, and lines kept summing to the total the tax authority stated.
/// </summary>
public sealed class CentRoundingTests
{
    /// <summary>The fourteen line amounts the portal answered for the Megapromet receipt, unrounded.</summary>
    private static readonly decimal[] s_megaprometLines =
    [
        9.75m, 2.59m, 7m, 8.22m, 1.85m, 2.5m, 1.4m, 3.9008m, 2.99m, 2.4196m, 5.7716m, 5.2m, 2.5m, 3.5595m,
    ];

    [Theory]
    [InlineData("3.9008", "3.90")]
    [InlineData("2.4196", "2.42")]
    [InlineData("5.7716", "5.77")]
    [InlineData("3.5595", "3.56")]
    [InlineData("0.125", "0.13")]
    [InlineData("0.005", "0.01")]
    [InlineData("7", "7.00")]
    public void Money_is_rounded_to_the_cent_half_away_from_zero(string exact, string cents)
        => Assert.Equal(decimal.Parse(cents, CultureInfo.InvariantCulture), CentRounding.ToCents(decimal.Parse(exact, CultureInfo.InvariantCulture)));

    [Fact]
    public void The_rounded_lines_of_a_real_receipt_are_what_the_receipt_prints()
    {
        var lines = CentRounding.ToCentsSummingTo(s_megaprometLines, 59.65m);

        Assert.Equal(
            [9.75m, 2.59m, 7.00m, 8.22m, 1.85m, 2.50m, 1.40m, 3.90m, 2.99m, 2.42m, 5.77m, 5.20m, 2.50m, 3.56m],
            lines);
        Assert.Equal(59.65m, lines.Sum());
    }

    [Fact]
    public void Lines_that_round_a_cent_over_the_total_give_the_cent_back()
    {
        // Each rounds up, to 0.34 + 0.34 + 0.33 = 1.01, against an exact sum and a stated total of 1.00.
        var lines = CentRounding.ToCentsSummingTo([0.335m, 0.335m, 0.33m], 1.00m);

        // The cent comes off the line rounding moved furthest up, the earlier of the two on a tie.
        Assert.Equal([0.33m, 0.34m, 0.33m], lines);
    }

    [Fact]
    public void Lines_that_round_a_cent_under_the_total_take_the_cent()
    {
        // Each rounds down, to 0.99, against an exact sum and a stated total of 1.00.
        var lines = CentRounding.ToCentsSummingTo([0.333m, 0.333m, 0.334m], 1.00m);

        Assert.Equal([0.33m, 0.33m, 0.34m], lines);
    }

    [Fact]
    public void Lines_that_disagree_with_the_total_for_real_are_only_rounded()
    {
        // 3.00 against 5.00 is not rounding. Forcing it to agree would hide a real discrepancy from
        // the arithmetic check.
        Assert.Equal([1.00m, 2.00m], CentRounding.ToCentsSummingTo([1.004m, 2m], 5.00m));
    }

    [Fact]
    public void Lines_with_no_total_are_only_rounded()
        => Assert.Equal([3.90m, 2.42m], CentRounding.ToCentsSummingTo([3.9008m, 2.4196m], null));

    [Fact]
    public void No_line_moves_by_more_than_a_cent_or_below_zero()
    {
        decimal[] exact = [0.005m, 0.005m, 0.005m, 0.005m, 0m, 0m];

        // 0.02 exact, so 0.02 stated. Each 0.005 rounds up to 0.01, which is 0.04: two cents too many.
        var lines = CentRounding.ToCentsSummingTo(exact, 0.02m);

        Assert.Equal(0.02m, lines.Sum());
        Assert.All(lines, line => Assert.True(line >= 0m));
        Assert.All(exact.Zip(lines), pair => Assert.True(Math.Abs(pair.First - pair.Second) <= 0.01m));
    }
}
