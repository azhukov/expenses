using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;
using Expenses.Infrastructure;
using Expenses.Infrastructure.Persistence;
using Expenses.Integration.Tests.Harness;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Persistence;

/// <summary>
/// The storage side of add-csv-export, over real PostgreSQL. ledger-export: "The ledger can be
/// exported for a chosen range" ("Export everything", "The last day is included whole", "Only a
/// start is given", "An export is not paged") and "An export is ordered as a spreadsheet reads".
///
/// Each test runs against a database of its own, because "every purchase" can only be asserted
/// over a ledger nothing else writes to.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PurchaseExportStorageTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Without_bounds_every_purchase_comes_oldest_first_with_its_expenses()
    {
        await using var ledger = await Ledger.Start(postgres, "export_all");
        await ledger.Given(Purchase.Record(At(2026, 10, 3), 3.00m, [Expense.Record("C", 3.00m)]));
        await ledger.Given(Purchase.Record(At(2025, 1, 1), 1.00m, [Expense.Record("A", 0.25m), Expense.Record("B", 0.75m)]));
        await ledger.Given(Purchase.Record(At(2026, 10, 2), 2.00m, [Expense.Record("B", 2.00m)]));

        var exported = await ledger.Export(null, null);

        Assert.Equal([1.00m, 2.00m, 3.00m], exported.Select(purchase => purchase.Amount));
        Assert.Equal(2, exported[0].Expenses.Count);
        Assert.All(exported, purchase => Assert.NotEmpty(purchase.Expenses));
    }

    [Fact]
    public async Task Purchases_at_the_same_moment_come_in_the_order_they_were_recorded()
    {
        await using var ledger = await Ledger.Start(postgres, "export_same_moment");
        var first = await ledger.Given(Purchase.Record(At(2026, 10, 1), 5.00m, [Expense.Record("A", 5.00m)]));
        var second = await ledger.Given(Purchase.Record(At(2026, 10, 1), 1.00m, [Expense.Record("B", 1.00m)]));

        var exported = await ledger.Export(null, null);

        Assert.Equal([first.Id, second.Id], exported.Select(purchase => purchase.Id));
    }

    [Fact]
    public async Task The_last_day_is_included_whole()
    {
        await using var ledger = await Ledger.Start(postgres, "export_last_day");
        await ledger.Given(Purchase.Record(At(2026, 10, 1, 0, 0, 0), 1.00m, [Expense.Record("A", 1.00m)]));
        await ledger.Given(Purchase.Record(At(2026, 10, 31, 23, 59, 30), 2.00m, [Expense.Record("B", 2.00m)]));
        await ledger.Given(Purchase.Record(At(2026, 11, 1, 0, 0, 0), 3.00m, [Expense.Record("C", 3.00m)]));
        await ledger.Given(Purchase.Record(At(2026, 9, 30, 23, 59, 59), 4.00m, [Expense.Record("D", 4.00m)]));

        var exported = await ledger.Export(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        Assert.Equal([1.00m, 2.00m], exported.Select(purchase => purchase.Amount));
    }

    [Fact]
    public async Task A_start_alone_and_an_end_alone_each_bound_their_side()
    {
        await using var ledger = await Ledger.Start(postgres, "export_one_bound");
        await ledger.Given(Purchase.Record(At(2026, 10, 14, 23, 59, 59), 1.00m, [Expense.Record("A", 1.00m)]));
        await ledger.Given(Purchase.Record(At(2026, 10, 15, 0, 0, 0), 2.00m, [Expense.Record("B", 2.00m)]));

        var fromOnly = await ledger.Export(new DateOnly(2026, 10, 15), null);
        var toOnly = await ledger.Export(null, new DateOnly(2026, 10, 14));

        Assert.Equal([2.00m], fromOnly.Select(purchase => purchase.Amount));
        Assert.Equal([1.00m], toOnly.Select(purchase => purchase.Amount));
    }

    [Fact]
    public async Task More_than_a_listing_page_is_returned_whole()
    {
        await using var ledger = await Ledger.Start(postgres, "export_unpaged");
        const int count = 230;

        using (var scope = ledger.Services.CreateScope())
        {
            var purchases = scope.ServiceProvider.GetRequiredService<IPurchaseRepository>();
            for (int i = 0; i < count; i++)
            {
                decimal amount = 1.00m + i;
                await purchases.Add(Purchase.Record(At(2026, 1, 1).AddHours(i), amount, [Expense.Record("Line", amount)]));
            }

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges();
        }

        var exported = await ledger.Export(null, null);

        Assert.Equal(count, exported.Count);
    }

    private static DateTime At(int year, int month, int day, int hour = 12, int minute = 0, int second = 0)
        => new(year, month, day, hour, minute, second, DateTimeKind.Unspecified);

    private sealed class Ledger(ServiceProvider services) : IAsyncDisposable
    {
        public ServiceProvider Services { get; } = services;

        public static async Task<Ledger> Start(PostgresFixture postgres, string database)
        {
            string connectionString = await postgres.ProvisionAnother(database);

            var services = new ServiceCollection()
                .AddLogging()
                .AddExpensesInfrastructure(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [$"ConnectionStrings:{ExpensesInfrastructure.ConnectionName}"] = connectionString,
                    })
                    .Build())
                .BuildServiceProvider();

            await services.PrepareExpensesDatabase(applyMigrations: true);

            return new Ledger(services);
        }

        public async Task<Purchase> Given(Purchase purchase)
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IPurchaseRepository>().Add(purchase);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges();

            return purchase;
        }

        /// <summary>Read in a fresh scope, so nothing the change tracker remembers can stand in for a row.</summary>
        public async Task<IReadOnlyList<Purchase>> Export(DateOnly? from, DateOnly? to)
        {
            using var scope = Services.CreateScope();

            return await scope.ServiceProvider.GetRequiredService<IPurchaseRepository>().ListForExport(from, to);
        }

        public ValueTask DisposeAsync() => Services.DisposeAsync();
    }
}
