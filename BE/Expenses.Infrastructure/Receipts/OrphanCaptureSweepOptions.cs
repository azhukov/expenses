namespace Expenses.Infrastructure.Receipts;

internal sealed class OrphanCaptureSweepOptions
{
    /// <summary>How often the sweep runs.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>How old an unconfirmed capture must be before the sweep removes it.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(1);
}
