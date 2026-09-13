using System.Globalization;
using Expenses.Desktop.Core.Rules;

namespace Expenses.Desktop.Core.Tests.Rules;

/// <summary>
/// Ported case for case from FE/src/format/format.test.ts (D7). Scenarios from desktop-client: "An
/// amount is formatted", "Formatting does not change values", "A recent date is described in relative
/// terms", "An older date is shown as a date".
/// </summary>
public sealed class FormattingTests
{
    private static readonly DateTime s_now = new(2026, 9, 3, 10, 0, 0);

    [Theory]
    [InlineData("12.5", "€12.50")]
    [InlineData("7", "€7.00")]
    [InlineData("0", "€0.00")]
    [InlineData("1234.56", "€1,234.56")]
    public void An_amount_is_formatted_in_euro_with_two_decimal_places(string amount, string expected)
    {
        Assert.Equal(expected, Formatting.Amount(decimal.Parse(amount, CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.05")]
    [InlineData("7")]
    [InlineData("12.5")]
    [InlineData("1234.56")]
    [InlineData("99999.99")]
    public void Formatting_does_not_change_values(string amount)
    {
        var value = decimal.Parse(amount, CultureInfo.InvariantCulture);
        var digits = new string([.. Formatting.Amount(value).Where(each => char.IsDigit(each) || each == '.')]);

        Assert.Equal(value, decimal.Parse(digits, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("2026-09-03T08:15:00", "Today")]
    [InlineData("2026-09-02T23:59:00", "Yesterday")]
    public void A_recent_date_is_described_in_relative_terms(string occurredAt, string expected)
    {
        Assert.Equal(expected, Formatting.When(Parse(occurredAt), s_now));
    }

    [Theory]
    [InlineData("2026-09-01T12:00:00", "1 Sept 2026")]
    [InlineData("2026-08-14T12:00:00", "14 Aug 2026")]
    public void An_older_date_is_shown_as_a_date(string occurredAt, string expected)
    {
        Assert.Equal(expected, Formatting.When(Parse(occurredAt), s_now));
    }

    [Fact]
    public void A_late_purchase_is_not_shifted_across_a_day_boundary()
    {
        Assert.Equal("Today", Formatting.When(Parse("2026-09-03T21:00:00"), new DateTime(2026, 9, 3, 23, 30, 0)));
    }

    [Fact]
    public void Wall_clock_time_is_never_converted_through_a_time_zone()
    {
        // A timestamp the ledger marked as UTC would still be read as the hour written.
        var utc = DateTime.SpecifyKind(Parse("2026-09-03T21:00:00"), DateTimeKind.Utc);

        Assert.Equal("Today", Formatting.When(utc, new DateTime(2026, 9, 3, 21, 30, 0, DateTimeKind.Local)));
    }

    private static DateTime Parse(string timestamp)
    {
        return DateTime.Parse(timestamp, CultureInfo.InvariantCulture);
    }
}
