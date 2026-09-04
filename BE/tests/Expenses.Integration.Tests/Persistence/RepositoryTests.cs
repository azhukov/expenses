using Expenses.Application.Abstractions;
using Expenses.Domain;
using Expenses.Integration.Tests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Persistence;

/// <summary>
/// The repository adapters behind the Application ports, over real PostgreSQL. Scenarios from
/// purchase-recording: "Purchases can be retrieved and listed", "Duplicate purchase submissions are
/// absorbed"; from reference-data: "Reference entries are identified by a stable code",
/// "Dictionaries are seeded and the seed is repeatable", "Merchants can be listed and searched".
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    /// <summary>
    /// Far from every other class's data, because the unique index on
    /// <c>(occurred_at, amount)</c> is shared by the whole suite.
    /// </summary>
    private static readonly DateTime Occurred = new(2031, 3, 14, 9, 0, 0, DateTimeKind.Unspecified);

    private ServiceProvider _services = null!;

    public Task InitializeAsync()
    {
        _services = postgres.Services();
        return postgres.Migrate();
    }

    public Task DisposeAsync() => _services.DisposeAsync().AsTask();

    [Fact]
    public async Task A_purchase_is_read_back_with_its_expenses()
    {
        var scope = _services.CreateScope();
        var purchases = scope.ServiceProvider.GetRequiredService<IPurchaseRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var purchase = Purchase.Record(
            At(1),
            8.48m,
            [Expense.Record("Sladoled", 4.49m), Expense.Record("Cokolada", 3.99m)],
            merchantRaw: "AROMA");

        await purchases.Add(purchase);
        await unitOfWork.SaveChanges();

        using var reading = _services.CreateScope();
        var stored = await reading.ServiceProvider.GetRequiredService<IPurchaseRepository>()
            .FindById(purchase.Id);

        Assert.NotNull(stored);
        Assert.Equal(2, stored.Expenses.Count);
        Assert.Equal("AROMA", stored.MerchantRaw);
    }

    [Fact]
    public async Task A_purchase_is_found_by_its_occurrence_and_amount()
    {
        await Given(Purchase.Record(At(2), 12.40m, [Expense.Record("Lunch", 12.40m)]));

        using var scope = _services.CreateScope();
        var found = await scope.ServiceProvider.GetRequiredService<IPurchaseRepository>()
            .FindByOccurrenceAndAmount(At(2), 12.40m);

        Assert.NotNull(found);
        Assert.Equal(12.40m, found.Amount);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IPurchaseRepository>()
            .FindByOccurrenceAndAmount(At(2), 12.50m));
    }

    [Fact]
    public async Task A_second_insert_of_the_same_pair_is_reported_as_a_duplicate()
    {
        await Given(Purchase.Record(At(3), 5.00m, [Expense.Record("Coffee", 5.00m)]));

        using var scope = _services.CreateScope();
        var purchases = scope.ServiceProvider.GetRequiredService<IPurchaseRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await purchases.Add(Purchase.Record(At(3), 5.00m, [Expense.Record("Coffee", 5.00m)]));

        // The unique violation is translated here, so the guard stays expressed in the use case
        // rather than an infrastructure exception surfacing through it (D4).
        var duplicate = await Assert.ThrowsAsync<DuplicatePurchaseException>(() => unitOfWork.SaveChanges());

        Assert.Equal(At(3), duplicate.OccurredAt);
        Assert.Equal(5.00m, duplicate.Amount);
    }

    [Fact]
    public async Task Purchases_are_listed_most_recent_first_within_a_date_range()
    {
        // Days of their own, because the range is what is under test and every other test in the
        // suite writes to the base day.
        var first = At(4).AddDays(30);
        await Given(Purchase.Record(first, 1.11m, [Expense.Record("One", 1.11m)]));
        await Given(Purchase.Record(first.AddDays(1), 2.22m, [Expense.Record("Two", 2.22m)]));
        await Given(Purchase.Record(first.AddDays(2), 3.33m, [Expense.Record("Three", 3.33m)]));

        // Outside the range on either side, so the bounds are asserted rather than assumed.
        await Given(Purchase.Record(first.AddDays(-1), 9.99m, [Expense.Record("Before", 9.99m)]));
        await Given(Purchase.Record(first.AddDays(3), 8.88m, [Expense.Record("After", 8.88m)]));

        using var scope = _services.CreateScope();
        var listed = await scope.ServiceProvider.GetRequiredService<IPurchaseRepository>()
            .List(new PurchaseListQuery(
                DateOnly.FromDateTime(first),
                DateOnly.FromDateTime(first.AddDays(2)),
                Skip: 0,
                Take: 10));

        Assert.Equal([3.33m, 2.22m, 1.11m], listed.Select(purchase => purchase.Amount));
        Assert.All(listed, purchase => Assert.NotEmpty(purchase.Expenses));
    }

    [Fact]
    public async Task A_listing_page_skips_and_takes()
    {
        var day = At(8).AddDays(60);
        await Given(Purchase.Record(day, 1.01m, [Expense.Record("One", 1.01m)]));
        await Given(Purchase.Record(day.AddHours(1), 2.02m, [Expense.Record("Two", 2.02m)]));
        await Given(Purchase.Record(day.AddHours(2), 3.03m, [Expense.Record("Three", 3.03m)]));

        using var scope = _services.CreateScope();
        var purchases = scope.ServiceProvider.GetRequiredService<IPurchaseRepository>();
        var listedDay = DateOnly.FromDateTime(day);

        var page = await purchases.List(new PurchaseListQuery(listedDay, listedDay, Skip: 1, Take: 1));

        Assert.Equal([2.02m], page.Select(purchase => purchase.Amount));
    }

    [Fact]
    public async Task Seeded_units_are_present_and_addressed_by_code()
    {
        using var scope = _services.CreateScope();
        var units = scope.ServiceProvider.GetRequiredService<IUnitRepository>();

        var kilogram = await units.FindByCode("KG");

        Assert.NotNull(kilogram);
        Assert.Equal("kg", kilogram.Symbol);
        Assert.Equal(Unit.UnitKind.Mass, kilogram.Kind);
        Assert.NotEmpty(await units.List(includeInactive: false));
    }

    [Fact]
    public async Task A_category_is_addressed_by_code_and_lists_its_active_children()
    {
        using var scope = _services.CreateScope();
        var categories = scope.ServiceProvider.GetRequiredService<ICategoryRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var groceries = Category.Create("TEST_GROCERIES", "Groceries");
        await categories.Add(groceries);
        await unitOfWork.SaveChanges();
        await categories.Add(Category.Create("TEST_PRODUCE", "Produce", groceries));
        var retired = Category.Create("TEST_RETIRED", "Retired", groceries);
        retired.Deactivate();
        await categories.Add(retired);
        await unitOfWork.SaveChanges();

        using var reading = _services.CreateScope();
        var repository = reading.ServiceProvider.GetRequiredService<ICategoryRepository>();

        Assert.NotNull(await repository.FindByCode("TEST_GROCERIES"));
        var children = await repository.ActiveChildrenOf(groceries.Id);
        Assert.Equal(["TEST_PRODUCE"], children.Select(child => child.Code));
    }

    [Fact]
    public async Task Merchants_are_matched_by_tax_number_and_searched_by_name()
    {
        using var scope = _services.CreateScope();
        var merchants = scope.ServiceProvider.GetRequiredService<IMerchantRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await merchants.Add(Merchant.Create("Kafiterija Šćepanović", "07440261"));
        await unitOfWork.SaveChanges();

        using var reading = _services.CreateScope();
        var repository = reading.ServiceProvider.GetRequiredService<IMerchantRepository>();

        Assert.NotNull(await repository.FindByTaxId("07440261"));
        Assert.NotNull(await repository.FindByName("Kafiterija Šćepanović"));

        // Accents are stripped on both sides, so a name typed without them still finds it (D14).
        var matches = await repository.Search("Scepanovic");

        Assert.Contains(matches, match => match.Merchant?.TaxId == "07440261");
    }

    [Fact]
    public async Task A_search_finds_verbatim_merchant_text_on_a_purchase()
    {
        await Given(Purchase.Record(
            At(9),
            4.20m,
            [Expense.Record("Kafa", 4.20m)],
            merchantRaw: "PIJACA ŠTAND 12"));

        using var scope = _services.CreateScope();
        var matches = await scope.ServiceProvider.GetRequiredService<IMerchantRepository>()
            .Search("STAND");

        Assert.Contains(matches, match => match.Merchant is null && match.MatchedText == "PIJACA ŠTAND 12");
    }

    private async Task Given(Purchase purchase)
    {
        using var scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPurchaseRepository>().Add(purchase);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges();
    }

    private static DateTime At(int minute) => Occurred.AddMinutes(minute);
}
