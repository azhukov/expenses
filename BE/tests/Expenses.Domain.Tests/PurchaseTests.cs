using Expenses.Domain.Entities;

namespace Expenses.Domain.Tests;

/// <summary>
/// Scenarios from purchase-recording: "Purchase is a container of expenses",
/// "Purchase amount reconciles with its expenses", "A purchase records where it was made".
/// </summary>
public sealed class PurchaseTests
{
    private static readonly DateTime s_occurred = new(2026, 8, 19, 14, 3, 0, DateTimeKind.Unspecified);

    [Fact]
    public void Manual_single_line_entry()
    {
        var purchase = Purchase.Record(
            new DateOnly(2026, 8, 19),
            amount: 12.40m,
            [Expense.Record("Lunch", amount: 12.40m)]);

        Assert.Equal(12.40m, purchase.Amount);
        Assert.Equal(new DateTime(2026, 8, 19, 0, 0, 0), purchase.OccurredAt);
        var expense = Assert.Single(purchase.Expenses);
        Assert.Equal("Lunch", expense.Description);
        Assert.Equal(12.40m, expense.Amount);
    }

    [Fact]
    public void Itemised_entry()
    {
        var purchase = Purchase.Record(
            s_occurred,
            amount: 80.00m,
            [
                Expense.Record("Shoes", amount: 60.00m),
                Expense.Record("Socks", amount: 18.50m),
                Expense.Record("Laces", amount: 1.50m),
            ]);

        Assert.Equal(3, purchase.Expenses.Count);
        Assert.Equal(80.00m, purchase.Amount);
    }

    [Fact]
    public void Purchase_with_no_expenses_is_rejected()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Purchase.Record(s_occurred, amount: 80.00m, []));

        Assert.Contains("at least one expense", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Amounts_reconcile()
    {
        var purchase = Purchase.Record(
            s_occurred,
            amount: 80.00m,
            [
                Expense.Record("Shoes", amount: 60.00m),
                Expense.Record("Socks", amount: 18.50m),
                Expense.Record("Laces", amount: 1.50m),
            ]);

        Assert.Equal(80.00m, purchase.Amount);
    }

    [Fact]
    public void Amounts_do_not_reconcile()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Purchase.Record(
            s_occurred,
            amount: 80.00m,
            [
                Expense.Record("Shoes", amount: 60.00m),
                Expense.Record("Socks", amount: 18.50m),
            ]));

        // The offending values are in the message: the domain holds entities only, so there is
        // no error type left to carry them as data.
        Assert.Contains("80.00", error.Message, StringComparison.Ordinal);
        Assert.Contains("78.50", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reconciliation_is_exact_not_approximate()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Purchase.Record(
            s_occurred,
            amount: 10.00m,
            [
                Expense.Record("Third", amount: 3.33m),
                Expense.Record("Third", amount: 3.33m),
                Expense.Record("Third", amount: 3.33m),
            ]));

        Assert.Contains("9.99", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Discount_does_not_affect_reconciliation()
    {
        var purchase = Purchase.Record(
            s_occurred,
            amount: 8.48m,
            [
                Expense.Record("Sladoled", amount: 4.49m, listUnitPrice: 8.50m, discountAmount: 4.01m),
                Expense.Record("Cokolada", amount: 3.99m, listUnitPrice: 7.50m, discountAmount: 3.51m),
            ]);

        Assert.Equal(8.48m, purchase.Amount);
    }

    [Fact]
    public void Savings_are_reported_for_a_purchase()
    {
        var purchase = Purchase.Record(
            s_occurred,
            amount: 8.48m,
            [
                Expense.Record("Sladoled", amount: 4.49m, listUnitPrice: 8.50m, discountAmount: 4.01m),
                Expense.Record("Cokolada", amount: 3.99m, listUnitPrice: 7.50m, discountAmount: 3.51m),
            ]);

        Assert.Equal(7.52m, purchase.TotalSaving);
    }

    [Fact]
    public void A_purchase_with_no_discounts_reports_no_saving()
    {
        var purchase = Purchase.Record(s_occurred, amount: 12.40m, [Expense.Record("Lunch", amount: 12.40m)]);

        Assert.Equal(0m, purchase.TotalSaving);
        Assert.Null(purchase.SavingPercentage);
    }

    [Fact]
    public void Purchase_with_an_unmatched_merchant()
    {
        var purchase = Purchase.Record(
            s_occurred,
            amount: 12.40m,
            [Expense.Record("Lunch", amount: 12.40m)],
            merchantRaw: "AROMA");

        Assert.Equal("AROMA", purchase.MerchantRaw);
        Assert.Null(purchase.MerchantId);
    }

    [Fact]
    public void Matching_later_does_not_erase_merchant_text()
    {
        var purchase = Purchase.Record(
            s_occurred,
            amount: 12.40m,
            [Expense.Record("Lunch", amount: 12.40m)],
            merchantRaw: "AROMA");

        purchase.MatchMerchant(11);

        Assert.Equal(11, purchase.MerchantId);
        Assert.Equal("AROMA", purchase.MerchantRaw);
    }

    [Fact]
    public void Purchase_with_no_merchant()
    {
        var purchase = Purchase.Record(s_occurred, amount: 12.40m, [Expense.Record("Lunch", amount: 12.40m)]);

        Assert.Null(purchase.MerchantId);
        Assert.Null(purchase.MerchantRaw);
    }

    [Fact]
    public void Confirming_candidates_replaces_the_expenses()
    {
        var purchase = Purchase.Record(s_occurred, amount: 8.48m, [Expense.Record("Unknown", amount: 8.48m)]);

        purchase.ReplaceExpenses([
            Expense.Record("Sladoled", amount: 4.49m),
            Expense.Record("Cokolada", amount: 3.99m),
        ]);

        Assert.Equal(2, purchase.Expenses.Count);
    }

    [Fact]
    public void Confirming_candidates_that_do_not_reconcile_leaves_the_expenses_unchanged()
    {
        var purchase = Purchase.Record(s_occurred, amount: 8.48m, [Expense.Record("Unknown", amount: 8.48m)]);

        var error = Assert.Throws<InvalidOperationException>(() => purchase.ReplaceExpenses([
            Expense.Record("Sladoled", amount: 4.49m),
            Expense.Record("Cokolada", amount: 4.99m),
        ]));

        Assert.Contains("8.48", error.Message, StringComparison.Ordinal);
        var expense = Assert.Single(purchase.Expenses);
        Assert.Equal("Unknown", expense.Description);
    }

    [Fact]
    public void Amounts_cannot_be_negative()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Purchase.Record(s_occurred, amount: -12.40m, [Expense.Record("Lunch", amount: 12.40m)]));

        Assert.Equal("amount", error.ParamName);
        Assert.Equal(-12.40m, error.ActualValue);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void No_timezone_conversion_is_applied(DateTimeKind kind)
    {
        var purchase = Purchase.Record(
            new DateTime(2026, 8, 19, 14, 3, 0, kind),
            amount: 12.40m,
            [Expense.Record("Lunch", amount: 12.40m)]);

        Assert.Equal(new DateTime(2026, 8, 19, 14, 3, 0), purchase.OccurredAt);
        Assert.Equal(DateTimeKind.Unspecified, purchase.OccurredAt.Kind);
    }
}
