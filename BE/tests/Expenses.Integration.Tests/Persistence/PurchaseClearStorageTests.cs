using Expenses.Application.Dtos;
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
/// The storage side of add-clear-all-expenses, over real PostgreSQL. purchase-recording: "The number
/// of recorded purchases can be read", "The ledger can be cleared of all purchases" ("Clearing a
/// populated ledger", "Reference data survives a clear", "An interrupted clear deletes nothing"), "A
/// clear must state how many purchases it expects to delete" ("The ledger changed since it was
/// counted"), and the lock of D2: a write in flight when the clear starts is counted, not lost.
///
/// Each test runs against a database of its own, because clearing the shared suite database would
/// delete what every other class arranged.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PurchaseClearStorageTests(PostgresFixture postgres)
{
    private static readonly DateTime s_occurred = new(2026, 8, 19, 10, 15, 0, DateTimeKind.Unspecified);

    [Fact]
    public async Task Count_spans_every_month()
    {
        await using var ledger = await Ledger.Start(postgres, "clear_count");
        await ledger.Given(Purchase.Record(s_occurred, 1.00m, [Expense.Record("A", 1.00m)]));
        await ledger.Given(Purchase.Record(s_occurred.AddMonths(-2), 2.00m, [Expense.Record("B", 2.00m)]));
        await ledger.Given(Purchase.Record(s_occurred.AddYears(-1), 3.00m, [Expense.Record("C", 3.00m)]));

        using var scope = ledger.Services.CreateScope();

        Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<IPurchaseRepository>().Count());
    }

    [Fact]
    public async Task Clearing_removes_purchases_and_their_expenses_and_names_what_went()
    {
        await using var ledger = await Ledger.Start(postgres, "clear_populated");
        var shared = new StoredReceiptFile("ab/shared", "image/jpeg", 10);
        var first = await ledger.Given(Purchase.Record(
            s_occurred, 3.00m, [Expense.Record("A", 1.00m), Expense.Record("B", 2.00m)], receipt: shared.AsReceipt(), extraction: Purchase.ExtractionState.Extracted));
        var second = await ledger.Given(Purchase.Record(
            s_occurred.AddDays(1), 4.00m, [Expense.Record("C", 4.00m)], receipt: shared.AsReceipt(), extraction: Purchase.ExtractionState.Extracted));
        var third = await ledger.Given(Purchase.Record(s_occurred.AddDays(2), 5.00m, [Expense.Record("D", 5.00m)]));

        PurchaseClearOutcome outcome;
        using (var scope = ledger.Services.CreateScope())
        {
            outcome = await scope.ServiceProvider.GetRequiredService<IPurchaseRepository>().DeleteAllIfCountIs(3);
        }

        Assert.True(outcome.Deleted);
        Assert.Equal(3, outcome.CountBefore);
        Assert.Equal([first.Id, second.Id, third.Id], outcome.PurchaseIds.Order());
        Assert.Equal(["ab/shared"], outcome.ReceiptStorageKeys);
        Assert.Equal(0, await ledger.Rows("purchases"));
        Assert.Equal(0, await ledger.Rows("expenses"));
    }

    [Fact]
    public async Task A_mismatch_deletes_nothing_and_reports_the_count()
    {
        await using var ledger = await Ledger.Start(postgres, "clear_mismatch");
        await ledger.Given(Purchase.Record(s_occurred, 1.00m, [Expense.Record("A", 1.00m)]));
        await ledger.Given(Purchase.Record(s_occurred.AddDays(1), 2.00m, [Expense.Record("B", 2.00m)]));

        using var scope = ledger.Services.CreateScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<IPurchaseRepository>().DeleteAllIfCountIs(1);

        Assert.False(outcome.Deleted);
        Assert.Equal(2, outcome.CountBefore);
        Assert.Empty(outcome.PurchaseIds);
        Assert.Equal(2, await ledger.Rows("purchases"));
        Assert.Equal(2, await ledger.Rows("expenses"));
    }

    [Fact]
    public async Task Reference_data_survives_a_clear()
    {
        await using var ledger = await Ledger.Start(postgres, "clear_reference");
        var merchant = await ledger.GivenMerchant(Merchant.Create("AROMA", "02440261"));
        await ledger.Given(Purchase.Record(
            s_occurred, 1.00m, [Expense.Record("A", 1.00m)], merchantId: merchant.Id, merchantRaw: "AROMA"));

        long categories = await ledger.Rows("categories");
        long units = await ledger.Rows("units");
        long aliases = await ledger.Rows("unit_aliases");

        using (var scope = ledger.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPurchaseRepository>().DeleteAllIfCountIs(1);
        }

        Assert.Equal(1, await ledger.Rows("merchants"));
        Assert.Equal(categories, await ledger.Rows("categories"));
        Assert.Equal(units, await ledger.Rows("units"));
        Assert.Equal(aliases, await ledger.Rows("unit_aliases"));
    }

    [Fact]
    public async Task A_write_in_flight_when_the_clear_starts_is_counted_not_lost()
    {
        await using var ledger = await Ledger.Start(postgres, "clear_in_flight");
        await ledger.Given(Purchase.Record(s_occurred, 1.00m, [Expense.Record("A", 1.00m)]));

        // Another writer has inserted but not committed, as a Record would be mid-save.
        await using var writer = ledger.Context();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        writer.Purchases.Add(Purchase.Record(s_occurred.AddDays(1), 2.00m, [Expense.Record("B", 2.00m)]));
        await writer.SaveChangesAsync();

        using var scope = ledger.Services.CreateScope();
        var clearing = scope.ServiceProvider.GetRequiredService<IPurchaseRepository>().DeleteAllIfCountIs(1);

        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(clearing.IsCompleted, "The clear must wait for the in-flight write rather than count around it.");

        await transaction.CommitAsync();
        var outcome = await clearing;

        Assert.False(outcome.Deleted);
        Assert.Equal(2, outcome.CountBefore);
        Assert.Equal(2, await ledger.Rows("purchases"));
    }

    [Fact]
    public async Task An_interrupted_clear_deletes_nothing()
    {
        await using var ledger = await Ledger.Start(postgres, "clear_interrupted");
        await ledger.Given(Purchase.Record(s_occurred, 1.00m, [Expense.Record("A", 1.00m)]));

        // Hold a conflicting lock so the clear is in progress, inside its transaction, when it is cancelled.
        await using var blocker = ledger.Context();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlRawAsync("LOCK TABLE purchases IN ROW EXCLUSIVE MODE");

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        using (var scope = ledger.Services.CreateScope())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                scope.ServiceProvider.GetRequiredService<IPurchaseRepository>().DeleteAllIfCountIs(1, cancellation.Token));
        }

        await transaction.RollbackAsync();

        Assert.Equal(1, await ledger.Rows("purchases"));
        Assert.Equal(1, await ledger.Rows("expenses"));
    }

    private sealed class Ledger(ServiceProvider services, string connectionString) : IAsyncDisposable
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

            return new Ledger(services, connectionString);
        }

        public ExpensesDbContext Context()
            => new(new DbContextOptionsBuilder<ExpensesDbContext>().UseNpgsql(connectionString).Options);

        public async Task<Purchase> Given(Purchase purchase)
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IPurchaseRepository>().Add(purchase);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges();

            return purchase;
        }

        public async Task<Merchant> GivenMerchant(Merchant merchant)
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IMerchantRepository>().Add(merchant);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges();

            return merchant;
        }

        /// <summary>Counted in SQL, so nothing the change tracker remembers can stand in for a row.</summary>
        public async Task<long> Rows(string table)
        {
            // Table names cannot be parameters, so each query is a literal.
            string sql = table switch
            {
                "purchases" => "select count(*) as \"Value\" from purchases",
                "expenses" => "select count(*) as \"Value\" from expenses",
                "merchants" => "select count(*) as \"Value\" from merchants",
                "categories" => "select count(*) as \"Value\" from categories",
                "units" => "select count(*) as \"Value\" from units",
                "unit_aliases" => "select count(*) as \"Value\" from unit_aliases",
                _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Not a table these tests count."),
            };

            await using var context = Context();

            return await context.Database.SqlQueryRaw<long>(sql).SingleAsync();
        }

        public ValueTask DisposeAsync() => Services.DisposeAsync();
    }
}
