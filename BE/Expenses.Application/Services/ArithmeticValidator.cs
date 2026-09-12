using Expenses.Application.Dtos;

namespace Expenses.Application.Services;

/// <summary>
/// The arithmetic oracle (D20). A pure function over an extraction result: it decides whether the
/// numbers were read correctly, replacing a confidence score a model reports about itself. It reads
/// nothing but the result and alters nothing at all. It lives here rather than in the domain
/// because it is not an entity, and the domain holds nothing else.
/// </summary>
public static class ArithmeticValidator
{
    /// <summary>Money is compared at the precision a receipt prints it.</summary>
    private const int ComparisonScale = 2;

    public static ArithmeticValidationReport Validate(ExtractionArithmetic result)
        => new([
            CheckLineSum(result),
            CheckLineDiscounts(result),
            CheckTotalTax(result),
        ]);

    private static ArithmeticCheck CheckLineSum(ExtractionArithmetic result)
    {
        if (result.Total is not { } total || result.Lines.Count == 0)
        {
            return ArithmeticCheck.NotApplicable(
                ArithmeticChecks.LineSum,
                "No total, or no lines, to sum against.");
        }

        decimal sum = result.Lines.Sum(line => line.Amount);
        bool agrees = Round(sum) == Round(total);

        return new ArithmeticCheck(
            ArithmeticChecks.LineSum,
            agrees ? CheckOutcome.Passed : CheckOutcome.Failed,
            agrees
                ? $"The lines sum to the extracted total {total}."
                : $"The extracted total is {total} while the lines sum to {sum}.",
            new Dictionary<string, decimal?>
            {
                ["total"] = total,
                ["computedSum"] = sum,
            });
    }

    private static ArithmeticCheck CheckLineDiscounts(ExtractionArithmetic result)
    {
        var checkable = result.Lines
            .Where(line => line.ListPrice.HasValue && line.Discount.HasValue)
            .ToList();

        if (checkable.Count == 0)
        {
            return ArithmeticCheck.NotApplicable(
                ArithmeticChecks.LineDiscount,
                "No line carries both a list price and a discount.");
        }

        foreach (var line in checkable)
        {
            decimal computed = line.ListPrice!.Value - line.Discount!.Value;
            if (Round(computed) == Round(line.Amount))
            {
                continue;
            }

            return new ArithmeticCheck(
                ArithmeticChecks.LineDiscount,
                CheckOutcome.Failed,
                $"Line {line.LineNumber} lists {line.ListPrice} less {line.Discount}, "
                + $"which is {computed}, but its amount is {line.Amount}.",
                new Dictionary<string, decimal?>
                {
                    ["lineNumber"] = line.LineNumber,
                    ["listPrice"] = line.ListPrice,
                    ["discount"] = line.Discount,
                    ["computedAmount"] = computed,
                    ["amount"] = line.Amount,
                });
        }

        return new ArithmeticCheck(
            ArithmeticChecks.LineDiscount,
            CheckOutcome.Passed,
            $"List price less discount equals the line amount on all {checkable.Count} discounted lines.",
            new Dictionary<string, decimal?> { ["checkedLines"] = checkable.Count });
    }

    private static ArithmeticCheck CheckTotalTax(ExtractionArithmetic result)
    {
        if (result.Total is not { } total
            || result.TaxRatePercent is not { } ratePercent
            || result.TaxAmount is not { } taxAmount)
        {
            return ArithmeticCheck.NotApplicable(
                ArithmeticChecks.TotalTax,
                "No total, tax rate and tax amount to check against one another.");
        }

        decimal rate = ratePercent / 100m;
        decimal computed = Round(total / (1m + rate) * rate);
        bool agrees = computed == Round(taxAmount);

        return new ArithmeticCheck(
            ArithmeticChecks.TotalTax,
            agrees ? CheckOutcome.Passed : CheckOutcome.Failed,
            agrees
                ? $"A total of {total} at {ratePercent}% implies the extracted tax {taxAmount}."
                : $"A total of {total} at {ratePercent}% implies tax of {computed}, but {taxAmount} was extracted.",
            new Dictionary<string, decimal?>
            {
                ["total"] = total,
                ["taxRatePercent"] = ratePercent,
                ["taxAmount"] = taxAmount,
                ["computedTaxAmount"] = computed,
            });
    }

    private static decimal Round(decimal value)
        => Math.Round(value, ComparisonScale, MidpointRounding.AwayFromZero);
}
