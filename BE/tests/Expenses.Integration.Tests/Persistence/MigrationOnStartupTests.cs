using Expenses.Infrastructure.Persistence;
using Expenses.Integration.Tests.Harness;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Expenses.Integration.Tests.Persistence;

/// <summary>
/// Scenarios from api-surface: "Applying migrations on startup is a setting".
/// </summary>
/// <remarks>
/// Each test starts the real HTTP host against a database of its own that has been provisioned but
/// never migrated, because whether the host migrated can only be seen on a database it was the
/// first to touch.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class MigrationOnStartupTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<ExpensesApi> _hosts = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task Development_migrates_by_default()
    {
        string connection = await postgres.ProvisionAnother("startup_development");

        Start(connection, environment: "Development");

        Assert.Empty(await PendingMigrations(connection));
    }

    [Fact]
    public async Task Other_environments_do_not_migrate_by_default()
    {
        string connection = await postgres.ProvisionAnother("startup_other_default");

        Start(connection, environment: "Testing");

        Assert.NotEmpty(await PendingMigrations(connection));
    }

    [Fact]
    public async Task The_setting_turns_migrating_on()
    {
        string connection = await postgres.ProvisionAnother("startup_setting_on");

        Start(connection, environment: "Testing", ("Database:ApplyMigrationsOnStartup", "true"));

        Assert.Empty(await PendingMigrations(connection));
    }

    [Fact]
    public async Task Provisioning_is_verified_either_way()
    {
        string connection = await WronglyProvisioned("startup_wrongly_provisioned");

        var error = Record.Exception(() =>
            Start(connection, environment: "Testing", ("Database:ApplyMigrationsOnStartup", "true")));

        Assert.NotNull(error);
        Assert.Contains("locale provider", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Starting the host is what runs <c>PrepareExpensesDatabase</c>; resolving its services forces that.</summary>
    private void Start(string connection, string environment, params (string Key, string Value)[] settings)
    {
        var host = new ExpensesApi(connection, settings) { Environment = environment };
        _hosts.Add(host);

        _ = host.Services;
    }

    private static async Task<IEnumerable<string>> PendingMigrations(string connection)
    {
        await using var context = new ExpensesDbContext(
            new DbContextOptionsBuilder<ExpensesDbContext>().UseNpgsql(connection).Options);

        return await context.Database.GetPendingMigrationsAsync();
    }

    private async Task<string> WronglyProvisioned(string name)
    {
        string maintenance = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Database = "postgres",
        }.ConnectionString;

        await using (var connection = new NpgsqlConnection(maintenance))
        {
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {name}", connection);
            await drop.ExecuteNonQueryAsync();
            await using var create = new NpgsqlCommand(
                $"CREATE DATABASE {name} ENCODING 'UTF8' LOCALE_PROVIDER libc LOCALE 'C' TEMPLATE template0",
                connection);
            await create.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = name }.ConnectionString;
    }
}
