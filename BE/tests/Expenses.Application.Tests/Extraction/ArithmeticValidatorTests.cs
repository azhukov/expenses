using Expenses.Application.Extraction;

namespace Expenses.Application.Tests.Extraction;

/// <summary>
/// Scenarios from receipt-ingestion: "An extraction result is validated arithmetically".
/// The oracle decides whether the numbers were read correctly (D20); it is not an estimate.
/// </summary>
public sealed class ArithmeticValidatorTests
{
    private static ArithmeticCheck Check(ArithmeticValidationReport report, string name) =>
        Assert.Single(report.Checks, check => check.Name == name);

    [Fact]
    public void A_result_that_adds_up()
    {
        var report = ArithmeticValidator.Validate(new ExtractionArithmetic(
            [new ExtractedLineAmounts(1, 4.49m), new ExtractedLineAmounts(2, 3.99m)],
            Total: 8.48m));

        Assert.Equal(CheckOutcome.Passed, Check(report, ArithmeticChecks.LineSum).Outcome);
        Assert.True(report.Passed);
    }

    [Fact]
    public void A_result_that_does_not_add_up()
    {
        var report = ArithmeticValidator.Validate(new ExtractionArithmetic(
            [new ExtractedLineAmounts(1, 4.49m), new ExtractedLineAmounts(2, 3.99m)],
            Total: 8.98m));

        var check = Check(report, ArithmeticChecks.LineSum);
        Assert.Equal(CheckOutcome.Failed, check.Outcome);
        Assert.Equal(8.98m, check.Values["total"]);
        Assert.Equal(8.48m, check.Values["computedSum"]);
        Assert.False(report.Passed);
    }

    [Fact]
    public void Discount_arithmetic_is_checked_per_line()
    {
        var report = ArithmeticValidator.Validate(new ExtractionArithmetic(
            [new ExtractedLineAmounts(1, 4.49m, ListPrice: 8.50m, Discount: 4.01m)],
            Total: 4.49m));

        Assert.Equal(CheckOutcome.Passed, Check(report, ArithmeticChecks.LineDiscount).Outcome);
    }

    [Fact]
    public void Discount_arithmetic_fails_for_one_line_only()
    {
        var report = ArithmeticValidator.Validate(new ExtractionArithmetic(
            [
                new ExtractedLineAmounts(1, 4.49m, ListPrice: 8.50m, Discount: 4.01m),
                new ExtractedLineAmounts(2, 3.99m, ListPrice: 7.50m, Discount: 3.01m),
            ],
            Total: 8.48m));

        var check = Check(report, ArithmeticChecks.LineDiscount);
        Assert.Equal(CheckOutcome.Failed, check.Outcome);
        Assert.Equal(2m, check.Values["lineNumber"]);
        Assert.Equal(4.49m, check.Values["computedAmount"]);
        Assert.Equal(3.99m, check.Values["amount"]);
    }

    [Fact]
    public void Tax_is_checked_against_the_total()
    {
        var report = ArithmeticValidator.Validate(new ExtractionArithmetic(
            [new ExtractedLineAmounts(1, 4.49m), new ExtractedLineAmounts(2, 3.99m)],
            Total: 8.48m,
            TaxRatePercent: 21m,
            TaxAmount: 1.47m));

        Assert.Equal(CheckOutcome.Passed, Check(report, ArithmeticChecks.TotalTax).Outcome);
        Assert.True(report.Passed);
    }

    [Fact]
    public void Tax_that_does_not_follow_from_the_total_fails()
    {
        var report = ArithmeticValidator.Validate(new ExtractionArithmetic(
            [new ExtractedLineAmounts(1, 8.48m)],
            Total: 8.48m,
            TaxRatePercent: 21m,
            TaxAmount: 1.78m));

        var check = Check(report, ArithmeticChecks.TotalTax);
        Assert.Equal(CheckOutcome.Failed, check.Outcome);
        Assert.Equal(1.78m, check.Values["taxAmount"]);
        Assert.Equal(1.47m, check.Values["computedTaxAmount"]);
    }

    [Fact]
    public void Checks_that_cannot_be_performed_are_not_failures()
    {
        var report = ArithmeticValidator.Validate(new ExtractionArithmetic(
            [new ExtractedLineAmounts(1, 4.49m), new ExtractedLineAmounts(2, 3.99m)],
            Total: 8.48m));

        Assert.Equal(CheckOutcome.NotApplicable, Check(report, ArithmeticChecks.LineDiscount).Outcome);
        Assert.Equal(CheckOutcome.NotApplicable, Check(report, ArithmeticChecks.TotalTax).Outcome);
        Assert.True(report.Passed);
        Assert.Equal(0, report.FailedCount);
    }

    [Fact]
    public void A_result_with_no_total_cannot_have_its_sum_checked()
    {
        var report = ArithmeticValidator.Validate(new ExtractionArithmetic(
            [new ExtractedLineAmounts(1, 4.49m)]));

        Assert.Equal(CheckOutcome.NotApplicable, Check(report, ArithmeticChecks.LineSum).Outcome);
        Assert.True(report.Passed);
    }

    [Fact]
    public void Every_check_is_reported_even_when_it_could_not_be_performed()
    {
        var report = ArithmeticValidator.Validate(new ExtractionArithmetic([]));

        Assert.Equal(3, report.Checks.Count);
        Assert.All(report.Checks, check => Assert.Equal(CheckOutcome.NotApplicable, check.Outcome));
    }

    [Fact]
    public void Failing_fewer_checks_makes_two_failed_results_comparable()
    {
        var worse = ArithmeticValidator.Validate(new ExtractionArithmetic(
            [new ExtractedLineAmounts(1, 4.49m, ListPrice: 8.50m, Discount: 3.01m)],
            Total: 9.98m,
            TaxRatePercent: 21m,
            TaxAmount: 9.99m));

        var better = ArithmeticValidator.Validate(new ExtractionArithmetic(
            [new ExtractedLineAmounts(1, 4.49m, ListPrice: 8.50m, Discount: 4.01m)],
            Total: 9.98m,
            TaxRatePercent: 21m,
            TaxAmount: 1.73m));

        Assert.Equal(3, worse.FailedCount);
        Assert.Equal(1, better.FailedCount);
    }

    [Fact]
    public void Validation_reads_only_the_result()
    {
        // The oracle is pure: the same input validates identically however often it is run.
        var result = new ExtractionArithmetic(
            [new ExtractedLineAmounts(1, 4.49m), new ExtractedLineAmounts(2, 3.99m)],
            Total: 8.48m);

        Assert.Equal(
            ArithmeticValidator.Validate(result).Checks,
            ArithmeticValidator.Validate(result).Checks);
    }
}
