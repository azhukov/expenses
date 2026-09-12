using Npgsql;
using Testcontainers.PostgreSql;

namespace Expenses.Integration.Tests.Harness;

/// <summary>
/// A real PostgreSQL, provisioned exactly as production is (D17). The behaviours that matter most
/// here — a unique index losing a race, <c>numeric</c> precision, ICU collation ordering, trigram
/// search — are ones an in-memory provider would report as passing while the real database failed.
///
/// The container's own database is the maintenance one; the database under test is created by
/// running the production provisioning script (D13) verbatim, because encoding, locale provider and
/// collation cannot be set by EF and cannot be changed afterwards.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string DatabaseName = "expenses";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("postgres")
        .WithUsername("expenses")
        .WithPassword("expenses")
        .Build();

    /// <summary>Points at the provisioned database, not at the maintenance one.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using (var maintenance = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await maintenance.OpenAsync();
            await using var provision = new NpgsqlCommand(await ProvisioningScript(), maintenance);
            await provision.ExecuteNonQueryAsync();
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = DatabaseName,
        }.ConnectionString;
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// A second empty database, provisioned by the same script under another name — what seeding
    /// tests need, since "the first run against an empty database" cannot be observed on a database
    /// the rest of the suite has already migrated and seeded.
    /// </summary>
    public async Task<string> ProvisionAnother(string name)
    {
        string script = (await ProvisioningScript()).Replace(
            $"CREATE DATABASE {DatabaseName}",
            $"CREATE DATABASE {name}",
            StringComparison.Ordinal);

        await using var maintenance = new NpgsqlConnection(_container.GetConnectionString());
        await maintenance.OpenAsync();

        await using (var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {name}", maintenance))
        {
            await drop.ExecuteNonQueryAsync();
        }

        await using (var create = new NpgsqlCommand(script, maintenance))
        {
            await create.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = name,
        }.ConnectionString;
    }

    /// <summary>
    /// Read from the repository rather than restated here: a second copy would be free to drift
    /// from the one that provisions production, which is the failure this guards against.
    /// </summary>
    private static async Task<string> ProvisioningScript()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);

        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Expenses.sln")))
        {
            repository = repository.Parent;
        }

        if (repository is null)
        {
            throw new InvalidOperationException(
                "The provisioning script could not be located: no Expenses.sln above the test output.");
        }

        return await File.ReadAllTextAsync(Path.Combine(repository.FullName, "db", "init", "01-create-database.sql"));
    }
}
