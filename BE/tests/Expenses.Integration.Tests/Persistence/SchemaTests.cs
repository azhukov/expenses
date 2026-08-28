using Expenses.Domain;
using Expenses.Integration.Tests.Harness;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Expenses.Integration.Tests.Persistence;

/// <summary>
/// Scenarios from purchase-recording: "Monetary amounts are exact", "Purchase time defaults to the
/// start of the day", "Duplicate purchase submissions are absorbed", "An expense line records what
/// it would have cost"; from receipt-ingestion: "The same image file is not stored twice"; and from
/// reference-data: "A merchant is identified by its tax identification number where one exists".
///
/// These are the behaviours D17 puts against real PostgreSQL because an in-memory provider would
/// report them as passing while the database rejected them.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SchemaTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTime Occurred = new(2026, 8, 24, 12, 50, 8, DateTimeKind.Unspecified);

    public Task InitializeAsync() => postgres.Migrate();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Numeric_precision_round_trips()
    {
        var purchase = Purchase.Record(
            At(1),
            1.06m,
            [Expense.Record(
                "Bananas",
                1.06m,
                quantity: 0.482m,
                unitPrice: 1.71m,
                listUnitPrice: 2.5000m,
                discountAmount: 1.4400m)]);

        var id = await Store(purchase);

        await using var reading = postgres.Context();
        var stored = await reading.Purchases
            .Include(read => read.Expenses)
            .SingleAsync(read => read.Id == id);

        var expense = Assert.Single(stored.Expenses);
        Assert.Equal(1.06m, stored.Amount);
        Assert.Equal(0.482m, expense.Quantity);
        Assert.Equal(1.71m, expense.UnitPrice);
        Assert.Equal(2.5000m, expense.ListUnitPrice);
        Assert.Equal(1.4400m, expense.DiscountAmount);
    }

    [Fact]
    public async Task Occurred_at_round_trips_unchanged_and_without_a_kind()
    {
        var id = await Store(Purchase.Record(At(2), 2.00m, [Expense.Record("Coffee", 2.00m)]));

        await using var reading = postgres.Context();
        var stored = await reading.Purchases.SingleAsync(read => read.Id == id);

        // No timezone conversion is ever applied, in either direction (D5).
        Assert.Equal(At(2), stored.OccurredAt);
        Assert.Equal(DateTimeKind.Unspecified, stored.OccurredAt.Kind);
    }

    [Fact]
    public async Task The_unique_index_rejects_a_second_purchase_with_the_same_occurrence_and_amount()
    {
        var occurred = At(3);
        await Store(Purchase.Record(occurred, 12.40m, [Expense.Record("Lunch", 12.40m)]));

        await using var second = postgres.Context();
        second.Purchases.Add(Purchase.Record(occurred, 12.40m, [Expense.Record("Lunch", 12.40m)]));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());

        // The index is what makes the guard true when two requests race; the application check
        // alone would lose exactly the case it exists for (D4).
        var violation = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, violation.SqlState);
    }

    [Fact]
    public async Task A_differing_amount_at_the_same_occurrence_is_accepted()
    {
        var occurred = At(4);
        await Store(Purchase.Record(occurred, 12.40m, [Expense.Record("Lunch", 12.40m)]));

        var second = await Store(Purchase.Record(occurred, 12.50m, [Expense.Record("Lunch", 12.50m)]));

        Assert.True(second > 0);
    }

    /// <summary>
    /// Two purchases may carry byte-identical receipts: they share one file in the store, so the
    /// hash is indexed but deliberately not unique. A unique index here would reject the second
    /// purchase outright (D11).
    /// </summary>
    [Fact]
    public async Task Two_purchases_may_reference_the_same_receipt_file()
    {
        var hash = Hash(7);
        var storageKey = "07/07/0707.jpg";

        var first = await Store(WithReceipt(At(7), hash, storageKey));
        var second = await Store(WithReceipt(At(8), hash, storageKey));

        await using var context = postgres.Context();
        var sharing = await context.Purchases
            .Where(purchase => purchase.Id == first || purchase.Id == second)
            .Select(purchase => purchase.Receipt!.StorageKey)
            .ToListAsync();

        Assert.Equal([storageKey, storageKey], sharing);
    }

    /// <summary>
    /// The receipt columns are set together or not at all: "has a receipt" is one fact rather than
    /// six independently nullable ones (D11).
    /// </summary>
    [Fact]
    public async Task A_partial_receipt_is_rejected_by_the_database()
    {
        await using var context = postgres.Context();

        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"Purchases\" (\"OccurredAt\", \"Amount\", \"ReceiptStorageKey\") "
            + "VALUES (timestamp '2031-01-01 00:00:00', 1.00, 'ab/cd/abcd.jpg')"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task A_null_discount_and_a_zero_discount_round_trip_as_distinct_states()
    {
        var id = await Store(Purchase.Record(
            At(5),
            3.00m,
            [
                Expense.Record("Undiscounted", 2.00m),
                Expense.Record("Discounted by nothing", 1.00m, listUnitPrice: 1.00m, discountAmount: 0.00m),
            ]));

        await using var reading = postgres.Context();
        var stored = await reading.Purchases
            .Include(read => read.Expenses)
            .SingleAsync(read => read.Id == id);

        var undiscounted = stored.Expenses.Single(expense => expense.Description == "Undiscounted");
        var zero = stored.Expenses.Single(expense => expense.Description == "Discounted by nothing");

        // A ledger that cannot tell "not discounted" from "discounted by nothing" reports a
        // misleading saving rate (D19).
        Assert.Null(undiscounted.DiscountAmount);
        Assert.Null(undiscounted.ListUnitPrice);
        Assert.Equal(0.00m, zero.DiscountAmount);
        Assert.Equal(1.00m, zero.ListUnitPrice);
    }

    [Fact]
    public async Task A_merchant_with_no_tax_number_coexists_with_one_that_has_one()
    {
        await using (var writing = postgres.Context())
        {
            writing.Merchants.Add(Merchant.Create("AROMA", "02440261"));
            writing.Merchants.Add(Merchant.Create("Pijaca Stall 12"));
            writing.Merchants.Add(Merchant.Create("Pijaca Stall 13"));
            await writing.SaveChangesAsync();
        }

        await using var reading = postgres.Context();
        var withoutTaxId = await reading.Merchants.CountAsync(merchant => merchant.TaxId == null);

        // Unique *when present*: several unidentified sellers must be able to coexist (D18).
        Assert.True(withoutTaxId >= 2);
        Assert.Equal(1, await reading.Merchants.CountAsync(merchant => merchant.TaxId == "02440261"));
    }

    [Fact]
    public async Task A_duplicate_tax_number_is_rejected()
    {
        await using (var writing = postgres.Context())
        {
            writing.Merchants.Add(Merchant.Create("VOLI", "03001234"));
            await writing.SaveChangesAsync();
        }

        await using var duplicate = postgres.Context();
        duplicate.Merchants.Add(Merchant.Create("VOLI 42", "03001234"));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());

        var violation = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, violation.SqlState);
    }

    /// <summary>A purchase carrying a receipt reference — the file itself is not this test's concern.</summary>
    private static Purchase WithReceipt(DateTime occurred, byte[] hash, string storageKey)
    {
        var purchase = Purchase.Record(occurred, 4.00m, [Expense.Record("Coffee", 4.00m)]);
        purchase.AttachReceipt(Receipt.Of(hash, storageKey, "image/jpeg", 1024));

        return purchase;
    }

    private async Task<long> Store(Purchase purchase)
    {
        await using var context = postgres.Context();
        context.Purchases.Add(purchase);
        await context.SaveChangesAsync();

        return purchase.Id;
    }

    /// <summary>
    /// A distinct occurrence per test, because the unique index on <c>(occurred_at, amount)</c> is
    /// shared by every test in the class and a collision would be someone else's failure.
    /// </summary>
    private static DateTime At(int minute) => Occurred.AddMinutes(minute);

    private static byte[] Hash(byte seed) => [.. Enumerable.Repeat(seed, Receipt.ContentHashLength)];
}
