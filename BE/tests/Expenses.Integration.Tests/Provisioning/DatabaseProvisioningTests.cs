using Expenses.Infrastructure.Persistence;
using Expenses.Integration.Tests.Harness;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Expenses.Integration.Tests.Provisioning;

/// <summary>
/// The one behaviour in the provisioning section (D13): a database created with the wrong encoding
/// or collation must fail loudly at startup. The risk of getting it wrong is that nothing appears
/// broken — sorting is quietly incorrect, and the fix is a dump and restore of the whole database.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseProvisioningTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_correctly_provisioned_database_passes_the_startup_check()
    {
        await using var context = postgres.Context();

        await DatabaseProvisioning.Verify(context);
    }

    [Fact]
    public async Task A_database_with_the_wrong_locale_provider_fails_loudly_at_startup()
    {
        var connection = await GivenDatabase(
            "wrongly_provisioned_locale",
            "ENCODING 'UTF8' LOCALE_PROVIDER libc LOCALE 'C' TEMPLATE template0");

        await using var context = Context(connection);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseProvisioning.Verify(context));

        // Loudly: the message has to name what is wrong, because the only fix is to recreate the
        // database and whoever reads it needs to know that before they try to alter it.
        Assert.Contains("locale provider", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_database_with_the_wrong_encoding_fails_loudly_at_startup()
    {
        var connection = await GivenDatabase(
            "wrongly_provisioned_encoding",
            "ENCODING 'SQL_ASCII' LOCALE_PROVIDER libc LOCALE 'C' TEMPLATE template0");

        await using var context = Context(connection);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseProvisioning.Verify(context));

        Assert.Contains("encoding", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SQL_ASCII", error.Message, StringComparison.Ordinal);
    }

    private async Task<string> GivenDatabase(string name, string clause)
    {
        var maintenance = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Database = "postgres",
        }.ConnectionString;

        await using (var connection = new NpgsqlConnection(maintenance))
        {
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {name}", connection);
            await drop.ExecuteNonQueryAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name} {clause}", connection);
            await create.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = name }.ConnectionString;
    }

    private static ExpensesDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<ExpensesDbContext>().UseNpgsql(connectionString).Options);
}
