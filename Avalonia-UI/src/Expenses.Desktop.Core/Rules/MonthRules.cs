using Expenses.Desktop.Core.Ledger;

namespace Expenses.Desktop.Core.Rules;

/// <summary>What the launcher derives from one month's read of purchases (ported from FE/src/month, D7).</summary>
public static class MonthRules
{
    /// <summary>How many entries a launcher shows: enough to recognise a purchase just made.</summary>
    public const int RecentCount = 8;

    /// <summary>
    /// The current calendar month against the computer's local date. The ledger stores wall-clock
    /// time, so the boundary is computed the same way, or a purchase made late on the last of the month
    /// lands outside it.
    /// </summary>
    public static (DateOnly From, DateOnly To) CurrentMonth(DateTime now)
    {
        var first = new DateOnly(now.Year, now.Month, 1);

        return (first, first.AddMonths(1).AddDays(-1));
    }

    /// <summary>
    /// The month to date, derived from the one response rather than an aggregation endpoint. The read
    /// is already scoped to the month; the filter keeps the figure honest if it ever is not. Over
    /// decimal there is no float accumulation to round away.
    /// </summary>
    public static decimal MonthTotal(IEnumerable<PurchaseView> purchases, DateTime now)
    {
        return purchases.Where(purchase => InMonth(purchase, now)).Sum(purchase => purchase.Amount);
    }

    /// <summary>
    /// How many receipts in the month still need a person to look at them. A receipt needing review
    /// from an earlier month is not counted, which is accepted rather than hidden.
    /// </summary>
    public static int ReviewCount(IEnumerable<PurchaseView> purchases, DateTime now)
    {
        return purchases.Count(purchase => purchase.ExtractionState == ExtractionState.NeedsReview && InMonth(purchase, now));
    }

    /// <summary>The few most recent, most recent first. Sorted here rather than trusted, then cut.</summary>
    public static IReadOnlyList<PurchaseView> Recent(IEnumerable<PurchaseView> purchases)
    {
        return [.. purchases.OrderByDescending(purchase => purchase.OccurredAt.Ticks).Take(RecentCount)];
    }

    private static bool InMonth(PurchaseView purchase, DateTime now)
    {
        return purchase.OccurredAt.Year == now.Year && purchase.OccurredAt.Month == now.Month;
    }
}
