using Expenses.Application.Dtos;
using Expenses.Application.Services;

namespace Expenses.Application.Tests.Extraction;

/// <summary>
/// The worked example from D20 — a real fiscalised receipt whose four independent checks agree —
/// and the same result with one digit altered in each checked position. A misread digit anywhere
/// in the numeric fields must break at least one check; that claim is what makes the oracle a
/// proof rather than an estimate, so it is tested position by position rather than asserted.
/// </summary>
public sealed class WorkedReceiptTests
{
    /// <summary>
    ///   4.49 + 3.99       == 8.48   lines sum to the total
    ///   8.50 - 4.01       == 4.49   list minus discount equals paid  (line 1)
    ///   7.50 - 3.51       == 3.99                                    (line 2)
    ///   8.48 / 1.21 * .21 == 1.47   total implies the printed VAT
    /// </summary>
    private static ExtractionArithmetic WorkedExample(
        decimal line1Amount = 4.49m,
        decimal line1ListPrice = 8.50m,
        decimal line1Discount = 4.01m,
        decimal line2Amount = 3.99m,
        decimal line2ListPrice = 7.50m,
        decimal line2Discount = 3.51m,
        decimal total = 8.48m,
        decimal taxRatePercent = 21m,
        decimal taxAmount = 1.47m)
        => new(
            [
                new ExtractedLineAmounts(1, line1Amount, line1ListPrice, line1Discount),
                new ExtractedLineAmounts(2, line2Amount, line2ListPrice, line2Discount),
            ],
            total,
            taxRatePercent,
            taxAmount);

    private static ArithmeticCheck Check(ArithmeticValidationReport report, string name)
        => Assert.Single(report.Checks, check => check.Name == name);

    [Fact]
    public void The_worked_example_passes_every_check()
    {
        var report = ArithmeticValidator.Validate(WorkedExample());

        Assert.Equal(CheckOutcome.Passed, Check(report, ArithmeticChecks.LineSum).Outcome);
        Assert.Equal(CheckOutcome.Passed, Check(report, ArithmeticChecks.LineDiscount).Outcome);
        Assert.Equal(CheckOutcome.Passed, Check(report, ArithmeticChecks.TotalTax).Outcome);
        Assert.True(report.Passed);
        Assert.Equal(0, report.FailedCount);
    }

    [Fact]
    public void A_digit_altered_in_the_first_line_amount_is_caught()
    {
        var report = ArithmeticValidator.Validate(WorkedExample(line1Amount: 4.99m));

        Assert.False(report.Passed);
        Assert.Equal(CheckOutcome.Failed, Check(report, ArithmeticChecks.LineSum).Outcome);
        Assert.Equal(CheckOutcome.Failed, Check(report, ArithmeticChecks.LineDiscount).Outcome);
    }

    [Fact]
    public void A_digit_altered_in_the_second_line_amount_is_caught()
    {
        var report = ArithmeticValidator.Validate(WorkedExample(line2Amount: 3.19m));

        Assert.False(report.Passed);
        Assert.Equal(CheckOutcome.Failed, Check(report, ArithmeticChecks.LineSum).Outcome);
        Assert.Equal(2m, Check(report, ArithmeticChecks.LineDiscount).Values["lineNumber"]);
    }

    [Fact]
    public void A_digit_altered_in_the_total_is_caught()
    {
        var report = ArithmeticValidator.Validate(WorkedExample(total: 8.98m));

        Assert.False(report.Passed);
        Assert.Equal(CheckOutcome.Failed, Check(report, ArithmeticChecks.LineSum).Outcome);
        Assert.Equal(CheckOutcome.Failed, Check(report, ArithmeticChecks.TotalTax).Outcome);
    }

    [Fact]
    public void A_digit_altered_in_a_list_price_is_caught()
    {
        var report = ArithmeticValidator.Validate(WorkedExample(line1ListPrice: 8.60m));

        Assert.False(report.Passed);
        var check = Check(report, ArithmeticChecks.LineDiscount);
        Assert.Equal(CheckOutcome.Failed, check.Outcome);
        Assert.Equal(1m, check.Values["lineNumber"]);
    }

    [Fact]
    public void A_digit_altered_in_a_discount_is_caught()
    {
        var report = ArithmeticValidator.Validate(WorkedExample(line1Discount: 4.11m));

        Assert.False(report.Passed);
        var check = Check(report, ArithmeticChecks.LineDiscount);
        Assert.Equal(CheckOutcome.Failed, check.Outcome);
        Assert.Equal(4.39m, check.Values["computedAmount"]);
    }

    [Fact]
    public void A_digit_altered_in_the_tax_amount_is_caught()
    {
        var report = ArithmeticValidator.Validate(WorkedExample(taxAmount: 1.87m));

        Assert.False(report.Passed);
        var check = Check(report, ArithmeticChecks.TotalTax);
        Assert.Equal(CheckOutcome.Failed, check.Outcome);
        Assert.Equal(1.47m, check.Values["computedTaxAmount"]);
    }

    [Fact]
    public void A_digit_altered_in_the_tax_rate_is_caught()
    {
        var report = ArithmeticValidator.Validate(WorkedExample(taxRatePercent: 25m));

        Assert.False(report.Passed);
        Assert.Equal(CheckOutcome.Failed, Check(report, ArithmeticChecks.TotalTax).Outcome);
    }

    [Fact]
    public void Only_the_checks_the_altered_digit_touches_fail()
    {
        // A list price is checked by exactly one of the four checks, so altering it must not
        // make the sum or the tax look wrong as well.
        var report = ArithmeticValidator.Validate(WorkedExample(line1ListPrice: 8.60m));

        Assert.Equal(CheckOutcome.Passed, Check(report, ArithmeticChecks.LineSum).Outcome);
        Assert.Equal(CheckOutcome.Passed, Check(report, ArithmeticChecks.TotalTax).Outcome);
        Assert.Equal(1, report.FailedCount);
    }
}
