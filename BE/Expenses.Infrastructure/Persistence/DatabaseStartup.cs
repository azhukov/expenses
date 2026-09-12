using Expenses.Infrastructure.Receipts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Infrastructure.Persistence;

/// <summary>
/// What both hosts do to their storage at startup: the database (D15) and the receipt store (D11).
/// Migrating on startup is a development convenience only: two hosts doing it in any other
/// environment would race, and neither should hold DDL rights at runtime — elsewhere a migration
/// bundle is a deployment step.
/// </summary>
public static class DatabaseStartup
{
    public static async Task PrepareExpensesDatabase(
        this IServiceProvider services,
        bool applyMigrations,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        if (applyMigrations)
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        // Always, in every environment: a database that was provisioned wrongly is not something
        // the ledger can correct at runtime, and it must not start quietly against one (D13).
        await DatabaseProvisioning.Verify(context, cancellationToken);

        // The receipt store is half of what the ledger is stored in now (D11), so it is checked
        // here, beside the database, rather than discovered at the first upload.
        ReceiptFileStore.Verify(scope.ServiceProvider.GetRequiredService<ReceiptStoreOptions>());

        // The temporary store is where every capture lands before it is confirmed, so it is
        // checked with the same urgency as the permanent one.
        TemporaryReceiptFileStore.Verify(scope.ServiceProvider.GetRequiredService<TemporaryReceiptStoreOptions>());
    }
}
