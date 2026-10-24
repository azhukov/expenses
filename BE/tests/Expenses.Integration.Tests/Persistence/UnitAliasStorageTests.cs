using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;
using Expenses.Infrastructure;
using Expenses.Infrastructure.Persistence;
using Expenses.Integration.Tests.Harness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Persistence;

/// <summary>
/// Scenarios from reference-data: "A starting set of unit aliases is provided once" — starting
/// aliases are present, a removed starting alias stays removed, a user alias survives restart —
/// and the storage side of "Unit aliases map printed text to a unit": one alias, one unit.
///
/// Each test runs against a database of its own, because what the migration inserts can only be
/// observed on a database it has just created, and restarting means migrating again.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UnitAliasStorageTests(PostgresFixture postgres)
{
    private const long Piece = 1;
    private const long Pack = 2;
    private const long Kilogram = 4;

    [Fact]
    public async Task Starting_aliases_are_present()
    {
        await using var services = await Started("alias_starting");

        var aliases = await Aliases(services);

        Assert.Equal(Piece, aliases["kom"]);
        Assert.Equal(Kilogram, aliases["kgr"]);
        Assert.Equal(12, aliases.Count);
    }

    [Fact]
    public async Task A_removed_starting_alias_stays_removed()
    {
        await using var services = await Started("alias_removed_stays");

        using (var scope = services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IUnitAliasRepository>();
            await repository.Remove((await repository.FindByAlias("st"))!);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges();
        }

        await Restart(services);

        Assert.DoesNotContain("st", (await Aliases(services)).Keys);
    }

    [Fact]
    public async Task A_user_alias_survives_restart()
    {
        await using var services = await Started("alias_user_survives");

        using (var scope = services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IUnitAliasRepository>().Add(UnitAlias.Create(Pack, "kut"));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges();
        }

        await Restart(services);

        Assert.Equal(Pack, (await Aliases(services))["kut"]);
    }

    [Fact]
    public async Task One_alias_one_unit()
    {
        await using var services = await Started("alias_unique");

        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IUnitAliasRepository>().Add(UnitAlias.Create(Pack, "kom"));

        // The application refuses this before it gets here; the index is what makes it true of
        // anything else that writes to the table.
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges());
    }

    private async Task<ServiceProvider> Started(string database)
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

        await Restart(services);

        return services;
    }

    /// <summary>What a start does to the database: migrate, then seed (D15).</summary>
    private static Task Restart(IServiceProvider services)
        => services.PrepareExpensesDatabase(applyMigrations: true);

    private static async Task<Dictionary<string, long>> Aliases(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var aliases = await scope.ServiceProvider.GetRequiredService<IUnitAliasRepository>().List();

        return aliases.ToDictionary(alias => alias.Alias, alias => alias.UnitId);
    }
}
