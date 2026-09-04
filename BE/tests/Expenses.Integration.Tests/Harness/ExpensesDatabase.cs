using Expenses.Infrastructure;
using Expenses.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Harness;

/// <summary>
/// Contexts over the provisioned container. Reading back through a second context is deliberate:
/// a value that survives only because it is still in the change tracker has not been shown to
/// round-trip through <c>numeric</c>, <c>timestamp</c> or <c>bytea</c> at all.
/// </summary>
public static class ExpensesDatabase
{
    public static ExpensesDbContext Context(this PostgresFixture fixture) =>
        new(new DbContextOptionsBuilder<ExpensesDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options);

    /// <summary>
    /// The composition root the hosts use, over the container. Tests resolve ports rather than
    /// constructing adapters, so the wiring itself is exercised rather than assumed (D1).
    /// </summary>
    public static ServiceProvider Services(
        this PostgresFixture fixture,
        params (string Key, string Value)[] settings)
    {
        var configuration = new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{ExpensesInfrastructure.ConnectionName}"] = fixture.ConnectionString,
        };

        foreach (var (key, value) in settings)
        {
            configuration[key] = value;
        }

        return new ServiceCollection()
            .AddLogging()
            .AddExpensesInfrastructure(new ConfigurationBuilder()
                .AddInMemoryCollection(configuration)
                .Build())
            .BuildServiceProvider();
    }

    /// <summary>
    /// Brings the schema up to the migrations in the repository, and seeds it. It goes through the
    /// composition root rather than a bare context on purpose: seeding is configured there, and a
    /// test database migrated any other way would be one no host would ever produce.
    /// </summary>
    public static async Task Migrate(this PostgresFixture fixture)
    {
        await using var services = fixture.Services();
        await services.PrepareExpensesDatabase(applyMigrations: true);
    }
}
