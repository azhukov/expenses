using Expenses.Application.Errors;
using Expenses.Application.Services;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain.Entities;

namespace Expenses.Application.Tests.Purchases;

/// <summary>Scenarios from purchase-recording: "Purchases can be retrieved and listed".</summary>
public sealed class PurchaseQueryTests
{
    private readonly InMemoryLedger _ledger = new();

    private PurchaseService Subject
        => new(_ledger, _ledger, _ledger, new MerchantService(_ledger, _ledger), _ledger, _ledger, _ledger);

    [Fact]
    public async Task Retrieve_a_purchase()
    {
        var stored = _ledger.Given(Purchase.Record(
            new DateTime(2026, 8, 19, 14, 3, 0, DateTimeKind.Unspecified),
            12.40m,
            [Expense.Record("Lunch", 12.40m)]));

        var purchase = await Subject.Get(stored.Id);

        Assert.Equal(stored.Id, purchase.Id);
        Assert.Single(purchase.Expenses);
    }

    [Fact]
    public async Task Retrieve_a_purchase_that_does_not_exist()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.Get(4711));

        Assert.Equal(ApplicationErrors.PurchaseNotFound, error.Error.Code);
        Assert.Equal(4711L, error.Error.Fields["id"]);
    }

    [Fact]
    public async Task List_by_date_range()
    {
        GivenPurchaseOn(new DateTime(2026, 7, 31, 23, 59, 0, DateTimeKind.Unspecified), 1.00m);
        GivenPurchaseOn(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Unspecified), 2.00m);
        GivenPurchaseOn(new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Unspecified), 3.00m);
        GivenPurchaseOn(new DateTime(2026, 8, 31, 23, 0, 0, DateTimeKind.Unspecified), 4.00m);
        GivenPurchaseOn(new DateTime(2026, 9, 1, 0, 1, 0, DateTimeKind.Unspecified), 5.00m);

        var listed = await Subject.List(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        Assert.Equal([4.00m, 3.00m, 2.00m], listed.Select(purchase => purchase.Amount));
    }

    [Fact]
    public async Task Listing_is_paged()
    {
        for (int day = 1; day <= 5; day++)
        {
            GivenPurchaseOn(new DateTime(2026, 8, day, 12, 0, 0, DateTimeKind.Unspecified), day);
        }

        var firstPage = await Subject.List(take: 2);
        var secondPage = await Subject.List(skip: 2, take: 2);

        Assert.Equal([5m, 4m], firstPage.Select(purchase => purchase.Amount));
        Assert.Equal([3m, 2m], secondPage.Select(purchase => purchase.Amount));
    }

    [Fact]
    public async Task A_page_size_beyond_the_maximum_is_rejected()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() =>
            Subject.List(take: PurchaseService.MaxPageSize + 1));

        Assert.Equal(ApplicationErrors.ListingPageSizeInvalid, error.Error.Code);
    }

    [Fact]
    public async Task A_range_that_ends_before_it_starts_is_rejected()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() =>
            Subject.List(new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 1)));

        Assert.Equal(ApplicationErrors.ListingRangeInvalid, error.Error.Code);
    }

    private void GivenPurchaseOn(DateTime occurredAt, decimal amount)
        => _ledger.Given(Purchase.Record(occurredAt, amount, [Expense.Record("Line", amount)]));
}
