namespace Expenses.Infrastructure.Persistence;

/// <summary>
/// The tag that separates the two questions a probe can ask: <em>is this process alive</em>, which
/// no dependency can answer for, and <em>can it serve</em>, which every tagged check has a say in.
/// A restarter that conflated them would kill a healthy host because the database was briefly gone.
/// </summary>
public static class HealthTags
{
    public const string Ready = "ready";
}
