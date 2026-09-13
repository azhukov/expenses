using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Rules;
using Expenses.Desktop.Core.Tests.Fakes;

namespace Expenses.Desktop.Core.Tests.Rules;

/// <summary>
/// Ported case for case from FE/src/month/month.test.ts (D7). Scenarios from desktop-client: "The
/// total reflects the current month", "A purchase late on the last day of the month is counted", "No
/// purchases this month", "Purchases needing review exist", "Nothing needs review", "Recent purchases
/// are shown".
/// </summary>
public sealed class MonthRulesTests
{
    private static readonly DateTime s_now = new(2026, 9, 17, 10, 0, 0);

    private static readonly PurchaseView[] s_month =
    [
        Purchases.Make(4, "2026-09-16T19:30:00", 12.5m, ExtractionState.NeedsReview),
        Purchases.Make(3, "2026-09-10T09:00:00", 30.25m, ExtractionState.Extracted),
        Purchases.Make(2, "2026-09-01T00:15:00", 7m, ExtractionState.NeedsReview),
        Purchases.Make(1, "2026-08-31T23:45:00", 100m, ExtractionState.Failed),
    ];

    [Theory]
    [InlineData("2026-09-17T10:00:00", "2026-09-01", "2026-09-30")]
    [InlineData("2026-01-05T00:00:00", "2026-01-01", "2026-01-31")]
    [InlineData("2028-02-05T00:00:00", "2028-02-01", "2028-02-29")]
    [InlineData("2026-09-30T23:59:00", "2026-09-01", "2026-09-30")]
    public void The_month_boundaries_follow_the_local_date(string now, string from, string to)
    {
        var range = MonthRules.CurrentMonth(DateTime.Parse(now, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), range.From);
        Assert.Equal(DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture), range.To);
    }

    [Fact]
    public void The_total_reflects_the_current_month()
    {
        Assert.Equal(49.75m, MonthRules.MonthTotal(s_month, s_now));
    }

    [Fact]
    public void A_purchase_late_on_the_last_day_of_the_month_is_counted()
    {
        var lastEvening = new DateTime(2026, 9, 30, 23, 50, 0);

        Assert.Equal(5m, MonthRules.MonthTotal([Purchases.Make(1, "2026-09-30T23:45:00", 5m)], lastEvening));
    }

    [Fact]
    public void No_purchases_this_month_totals_zero()
    {
        Assert.Equal(0m, MonthRules.MonthTotal([], s_now));
        Assert.Equal(0m, MonthRules.MonthTotal([Purchases.Make(1, "2026-08-31T23:45:00", 100m)], s_now));
    }

    [Fact]
    public void A_total_of_cents_keeps_exactly_two_decimal_places()
    {
        // The float accumulation the browser client rounds away cannot occur over decimal (D7).
        PurchaseView[] cents = [.. Enumerable.Range(1, 10).Select(each => Purchases.Make(each, "2026-09-10T10:00:00", 0.1m))];

        Assert.Equal(1.0m, MonthRules.MonthTotal(cents, s_now));
    }

    [Fact]
    public void Purchases_needing_review_exist()
    {
        Assert.Equal(2, MonthRules.ReviewCount(s_month, s_now));
    }

    [Fact]
    public void Nothing_needs_review()
    {
        Assert.Equal(0, MonthRules.ReviewCount([Purchases.Make(3, "2026-09-10T09:00:00", 30.25m, ExtractionState.Extracted)], s_now));
        Assert.Equal(0, MonthRules.ReviewCount([], s_now));
        Assert.Equal(0, MonthRules.ReviewCount([Purchases.Make(1, "2026-08-31T23:45:00", 100m, ExtractionState.NeedsReview)], s_now));
    }

    [Fact]
    public void Recent_purchases_are_most_recent_first()
    {
        Assert.Equal([4L, 3L, 2L, 1L], MonthRules.Recent(s_month).Select(each => each.Id));
    }

    [Fact]
    public void A_response_that_did_not_arrive_sorted_is_reordered()
    {
        Assert.Equal([4L, 3L, 2L], MonthRules.Recent([s_month[2], s_month[0], s_month[1]]).Select(each => each.Id));
    }

    [Fact]
    public void Only_the_few_that_fit_on_a_launcher_are_taken()
    {
        PurchaseView[] many = [.. Enumerable.Range(1, 20).Select(each => Purchases.Make(each, $"2026-09-{each:00}T10:00:00", 1m))];

        var recent = MonthRules.Recent(many);

        Assert.Equal(8, recent.Count);
        Assert.Equal(20, recent[0].Id);
    }
}
