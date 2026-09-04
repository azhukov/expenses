using System.Globalization;

namespace Expenses.Domain.Tests;

/// <summary>
/// Scenarios from purchase-recording ("Expense line detail", "An expense line records what it
/// would have cost") and reference-data ("Expenses may reference reference data loosely").
/// </summary>
public sealed class ExpenseTests
{
    [Fact]
    public void Quantity_defaults_to_one()
    {
        var expense = Expense.Record("Lunch", amount: 12.40m);

        Assert.Equal(1m, expense.Quantity);
    }

    [Fact]
    public void Fractional_quantity()
    {
        var expense = Expense.Record(
            "Bananas",
            amount: 1.06m,
            quantity: 0.482m,
            unitId: 7,
            unitPrice: 2.19m);

        Assert.Equal(0.482m, expense.Quantity);
        Assert.Equal(7, expense.UnitId);
        Assert.Equal(2.19m, expense.UnitPrice);
        Assert.Equal(1.06m, expense.Amount);
    }

    [Fact]
    public void Quantity_beyond_three_decimal_places_is_rejected()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Expense.Record("Bananas", amount: 1.06m, quantity: 0.4825m));

        Assert.Equal("quantity", error.ParamName);
        Assert.Contains("at most 3 decimal places", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Negative_quantity_is_rejected()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Expense.Record("Bananas", amount: 1.06m, quantity: -1m));

        Assert.Equal("quantity", error.ParamName);
        Assert.Equal(-1m, error.ActualValue);
    }

    [Fact]
    public void Quantity_and_unit_price_need_not_multiply_to_the_amount()
    {
        var expense = Expense.Record("Rolls", amount: 1.00m, quantity: 3m, unitPrice: 0.33m);

        Assert.Equal(1.00m, expense.Amount);
    }

    [Fact]
    public void Description_is_required()
    {
        var error = Assert.Throws<ArgumentException>(() => Expense.Record("  ", amount: 1.00m));

        Assert.Equal("description", error.ParamName);
    }

    [Fact]
    public void Expense_without_a_category()
    {
        var expense = Expense.Record("Lunch", amount: 12.40m);

        Assert.Null(expense.CategoryId);
        Assert.Null(expense.CategoryRaw);
    }

    [Fact]
    public void Matched_reference_and_verbatim_text_coexist()
    {
        var expense = Expense.Record("Bananas", amount: 1.06m, unitId: 7, unitRaw: "kg.");

        Assert.Equal(7, expense.UnitId);
        Assert.Equal("kg.", expense.UnitRaw);
    }

    [Fact]
    public void Matching_later_does_not_erase_verbatim_text()
    {
        var expense = Expense.Record("Carrots", amount: 1.20m, unitRaw: "Bund", categoryRaw: "Povrce");

        expense.MatchUnit(7);
        expense.MatchCategory(3);

        Assert.Equal("Bund", expense.UnitRaw);
        Assert.Equal("Povrce", expense.CategoryRaw);
        Assert.Equal(7, expense.UnitId);
        Assert.Equal(3, expense.CategoryId);
    }

    [Fact]
    public void Non_latin_receipt_text_is_retained_character_for_character()
    {
        const string description = "Хлеб ржаной 500г";

        var expense = Expense.Record(description, amount: 1.15m);

        Assert.Equal(description, expense.Description);
    }

    [Fact]
    public void Discounted_line()
    {
        var expense = Expense.Record(
            "Sladoled Milka Mini Sticks",
            amount: 4.49m,
            listUnitPrice: 8.50m,
            discountAmount: 4.01m);

        Assert.Equal(4.49m, expense.Amount);
        Assert.Equal(8.50m, expense.ListUnitPrice);
        Assert.Equal(4.01m, expense.DiscountAmount);
    }

    [Fact]
    public void Amount_is_not_recomputed_from_list_price_and_discount()
    {
        var expense = Expense.Record(
            "Sladoled Milka Mini Sticks",
            amount: 4.49m,
            listUnitPrice: 8.50m,
            discountAmount: 4.00m);

        Assert.Equal(4.49m, expense.Amount);
        Assert.NotEqual(4.50m, expense.Amount);
    }

    [Fact]
    public void Discount_percentage_is_derived_not_stored()
    {
        var expense = Expense.Record("Sladoled", amount: 4.49m, listUnitPrice: 8.50m, discountAmount: 4.01m);

        Assert.Equal(47.18m, expense.DiscountPercentage);
    }

    [Fact]
    public void Absence_of_a_discount_is_distinguishable_from_a_zero_discount()
    {
        var undiscounted = Expense.Record("Bread", amount: 1.15m);
        var discountedByNothing = Expense.Record("Milk", amount: 1.15m, listUnitPrice: 1.15m, discountAmount: 0.00m);

        Assert.Null(undiscounted.DiscountAmount);
        Assert.Null(undiscounted.DiscountPercentage);
        Assert.Equal(0m, discountedByNothing.DiscountAmount);
        Assert.Equal(0m, discountedByNothing.DiscountPercentage);
    }

    [Fact]
    public void Negative_discount_is_rejected()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Expense.Record(
            "Sladoled",
            amount: 4.49m,
            listUnitPrice: 8.50m,
            discountAmount: -4.01m));

        Assert.Equal("discountAmount", error.ParamName);
        Assert.Contains("positive magnitude", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_discount_without_a_list_price_is_rejected()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            Expense.Record("Sladoled", amount: 4.49m, discountAmount: 4.01m));

        Assert.Equal("listUnitPrice", error.ParamName);
    }

    [Fact]
    public void A_list_price_without_a_discount_is_rejected()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            Expense.Record("Sladoled", amount: 4.49m, listUnitPrice: 8.50m));

        Assert.Equal("discountAmount", error.ParamName);
    }

    [Fact]
    public void Amounts_cannot_be_negative()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Expense.Record("Lunch", amount: -12.40m));

        Assert.Equal("amount", error.ParamName);
        Assert.Equal(-12.40m, error.ActualValue);
    }

    // The monetary rules of D7, formerly asserted against a shared validator that no longer
    // exists. The rule is unchanged; it is now stated by the entity that owns the fields.

    [Fact]
    public void Precision_is_preserved()
    {
        var expense = Expense.Record("Bananas", amount: 1.06m, unitPrice: 1.71m);

        Assert.Equal("1.71", expense.UnitPrice!.Value.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Zero_is_a_valid_amount()
    {
        Assert.Equal(0m, Expense.Record("Carrier bag", amount: 0m).Amount);
    }

    [Fact]
    public void More_than_two_decimal_places_is_rejected()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Expense.Record("Bananas", amount: 1.06m, unitPrice: 1.719m));

        Assert.Equal("unitPrice", error.ParamName);
        Assert.Contains("at most 2 decimal places", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Trailing_zeros_beyond_two_places_are_dropped_rather_than_rejected()
    {
        var expense = Expense.Record("Lunch", amount: 12.400000m);

        Assert.Equal("12.40", expense.Amount.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void An_absent_optional_amount_is_left_alone()
    {
        Assert.Null(Expense.Record("Lunch", amount: 12.40m).DiscountAmount);
    }
}
