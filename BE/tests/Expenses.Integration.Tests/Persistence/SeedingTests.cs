using Expenses.Application.Abstractions;
using Expenses.Domain;
using Expenses.Infrastructure;
using Expenses.Infrastructure.Persistence;
using Expenses.Integration.Tests.Harness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Persistence;

/// <summary>
/// Scenarios from reference-data: "Dictionaries are seeded and the seed is repeatable" — first run,
/// repeated seeding, user changes survive, user-created entries survive — and "Merchants are a
/// dictionary learned during ingestion": the merchant dictionary is empty after seeding while
/// categories and units are not (D15, D18).
///
/// Each test runs against a database of its own, because "the first run against an empty database"
/// cannot be observed on one the rest of the suite has already seeded.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SeedingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task First_run()
    {
        await using var services = await Seeded("seed_first_run");

        using var scope = services.CreateScope();
        var categories = await scope.ServiceProvider.GetRequiredService<ICategoryRepository>().List(true);
        var units = await scope.ServiceProvider.GetRequiredService<IUnitRepository>().List(true);

        Assert.NotEmpty(categories);
        Assert.NotEmpty(units);
        Assert.All(categories, category => Assert.True(category.IsSystem));
    }

    [Fact]
    public async Task Repeated_seeding()
    {
        await using var services = await Seeded("seed_repeated");
        var first = await Codes(services);

        await Migrate(services);

        Assert.Equal(first, await Codes(services));
    }

    [Fact]
    public async Task User_changes_survive_seeding()
    {
        await using var services = await Seeded("seed_user_rename");

        var renamed = await Rename(services);
        await Migrate(services);

        using var scope = services.CreateScope();
        var category = await scope.ServiceProvider.GetRequiredService<ICategoryRepository>()
            .FindByCode(renamed.Code);

        // Seeding upserts by code precisely so a rename is not reverted on the next run (D8, D15).
        Assert.Equal("Renamed by the user", category?.Name);
    }

    [Fact]
    public async Task User_created_entries_survive_seeding()
    {
        await using var services = await Seeded("seed_user_created");

        using (var scope = services.CreateScope())
        {
            var categories = scope.ServiceProvider.GetRequiredService<ICategoryRepository>();
            await categories.Add(Category.Create("MY_OWN", "Something of my own"));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges();
        }

        await Migrate(services);

        using var reading = services.CreateScope();
        var mine = await reading.ServiceProvider.GetRequiredService<ICategoryRepository>()
            .FindByCode("MY_OWN");

        Assert.NotNull(mine);
        Assert.False(mine.IsSystem);
    }

    [Fact]
    public async Task Merchants_are_not_seeded()
    {
        await using var services = await Seeded("seed_no_merchants");

        using var scope = services.CreateScope();
        var merchants = await scope.ServiceProvider.GetRequiredService<IMerchantRepository>().List(true);
        var categories = await scope.ServiceProvider.GetRequiredService<ICategoryRepository>().List(true);

        // No merchant ships with the product: the dictionary is learned during ingestion (D18).
        Assert.Empty(merchants);
        Assert.NotEmpty(categories);
    }

    private async Task<ServiceProvider> Seeded(string database)
    {
        string connectionString = await postgres.ProvisionAnother(database);

        var services = new ServiceCollection()
            .AddExpensesInfrastructure(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"ConnectionStrings:{ExpensesInfrastructure.ConnectionName}"] = connectionString,
                })
                .Build())
            .BuildServiceProvider();

        await Migrate(services);

        return services;
    }

    /// <summary>Migrating is what runs seeding, in development and in the suite alike (D15).</summary>
    private static Task Migrate(IServiceProvider services)
        => services.PrepareExpensesDatabase(applyMigrations: true);

    private static async Task<List<string>> Codes(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var categories = await scope.ServiceProvider.GetRequiredService<ICategoryRepository>().List(true);

        return [.. categories.Select(category => category.Code).Order()];
    }

    private static async Task<Category> Rename(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();
        var category = await context.Categories.OrderBy(entry => entry.Code).FirstAsync();

        category.Rename("Renamed by the user");
        await context.SaveChangesAsync();

        return category;
    }
}
