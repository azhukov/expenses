using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Expenses.Infrastructure.Persistence;

/// <summary>
/// Asserts that the database was created with the encoding, locale provider and collation the
/// ledger needs (D13). EF cannot set any of the three and none can be altered afterwards without a
/// dump and restore, so a mis-provisioned environment has to fail at startup: left alone it looks
/// entirely healthy and merely sorts mixed-language content wrongly.
/// </summary>
public static class DatabaseProvisioning
{
    private const string RequiredEncoding = "UTF8";

    /// <summary>How <c>pg_database.datlocprovider</c> spells the ICU provider.</summary>
    private const char IcuProvider = 'i';

    private const string RequiredLocale = "und";

    public static async Task Verify(ExpensesDbContext context, CancellationToken cancellationToken = default)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        bool opened = connection.State != System.Data.ConnectionState.Open;

        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT pg_encoding_to_char(encoding), datlocprovider, datlocale, datcollate
                FROM pg_database
                WHERE datname = current_database()
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "The current database has no row in pg_database, so its provisioning cannot be checked.");
            }

            string encoding = reader.GetString(0);
            char provider = reader.GetChar(1);
            string locale = reader.IsDBNull(2) ? reader.GetString(3) : reader.GetString(2);

            if (encoding != RequiredEncoding)
            {
                throw Misprovisioned(
                    $"its encoding is {encoding} rather than {RequiredEncoding}");
            }

            if (provider != IcuProvider)
            {
                throw Misprovisioned(
                    $"its locale provider is '{provider}' rather than ICU, so mixed-language content "
                    + "would sort by raw byte order");
            }

            if (!locale.StartsWith(RequiredLocale, StringComparison.Ordinal))
            {
                throw Misprovisioned($"its ICU locale is '{locale}' rather than '{RequiredLocale}'");
            }
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// Names the fix as well as the fault, because the fix is not "alter the database" — none of
    /// these can be changed in place, and someone reading this needs to know that first (D13).
    /// </summary>
    private static InvalidOperationException Misprovisioned(string fault)
        => new($"This database was not provisioned for the ledger: {fault}. "
            + "Encoding, locale provider and collation are fixed when a database is created and "
            + "cannot be altered afterwards, so recreate it with db/init/01-create-database.sql "
            + "and restore the data into the new database.");
}
