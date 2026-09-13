namespace Expenses.Desktop.Core.Tests.Fakes;

/// <summary>A clock stopped at one local wall-clock time, in a zone with no offset so local is what was written.</summary>
public sealed class FixedClock(DateTime localNow) : TimeProvider
{
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow()
    {
        return new DateTimeOffset(DateTime.SpecifyKind(localNow, DateTimeKind.Utc));
    }
}
