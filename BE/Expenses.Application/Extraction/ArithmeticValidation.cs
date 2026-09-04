using Expenses.Domain.Extraction;

namespace Expenses.Application.Extraction;

/// <summary>The outcome of one arithmetic check. "Not applicable" is not a failure (D20).</summary>
public enum CheckOutcome
{
    NotApplicable = 0,
    Passed = 1,
    Failed = 2,
}

/// <summary>Names of the checks, stable because they are reported to both adapters.</summary>
public static class ArithmeticChecks
{
    public const string LineSum = "line_sum";
    public const string LineDiscount = "line_discount";
    public const string TotalTax = "total_tax";
}

/// <summary>
/// One line of an extraction result, reduced to the numbers arithmetic can decide.
/// <paramref name="LineNumber"/> is 1-based so a failure can name the line as a human would.
/// </summary>
public sealed record ExtractedLineAmounts(
    int LineNumber,
    decimal Amount,
    decimal? ListPrice = null,
    decimal? Discount = null)
{
    /// <summary>Reduces one candidate to the numbers the oracle can decide (D20).</summary>
    public static ExtractedLineAmounts From(ExtractionCandidate candidate) => new(
        candidate.LineNumber,
        candidate.Amount,
        candidate.ListUnitPrice,
        candidate.DiscountAmount);
}

/// <summary>The numeric content of an extraction result, and nothing else.</summary>
public sealed record ExtractionArithmetic(
    IReadOnlyList<ExtractedLineAmounts> Lines,
    decimal? Total = null,
    decimal? TaxRatePercent = null,
    decimal? TaxAmount = null)
{
    /// <summary>Reduces an extraction result to the numbers the oracle can decide (D20).</summary>
    public static ExtractionArithmetic From(ExtractionResult result) => new(
        [.. result.Candidates.Select(ExtractedLineAmounts.From)],
        result.Total,
        result.TaxRatePercent,
        result.TaxAmount);
}

/// <summary>
/// One check, carrying the values that disagreed so the reason survives to the user
/// rather than being reduced to a boolean.
/// </summary>
public sealed record ArithmeticCheck(
    string Name,
    CheckOutcome Outcome,
    string Description,
    IReadOnlyDictionary<string, decimal?> Values)
{
    private static readonly IReadOnlyDictionary<string, decimal?> NoValues =
        new Dictionary<string, decimal?>();

    public static ArithmeticCheck NotApplicable(string name, string description)
        => new(name, CheckOutcome.NotApplicable, description, NoValues);

    // Records compare a dictionary field by reference, which would make two identically
    // computed reports unequal. The values are part of the check, so they are compared as such.
    public bool Equals(ArithmeticCheck? other)
        => other is not null
        && Name == other.Name
        && Outcome == other.Outcome
        && Description == other.Description
        && Values.Count == other.Values.Count
        && Values.All(entry => other.Values.TryGetValue(entry.Key, out decimal? value) && value == entry.Value);

    public override int GetHashCode() => HashCode.Combine(Name, Outcome, Description, Values.Count);
}

/// <summary>The full report. Failing fewer checks is what makes two failed results comparable (D20).</summary>
public sealed record ArithmeticValidationReport(IReadOnlyList<ArithmeticCheck> Checks)
{
    /// <summary>True when no check failed. Checks that could not be performed do not count against it.</summary>
    public bool Passed => Checks.All(check => check.Outcome != CheckOutcome.Failed);

    public int FailedCount => Checks.Count(check => check.Outcome == CheckOutcome.Failed);

    public IEnumerable<ArithmeticCheck> Failures
        => Checks.Where(check => check.Outcome == CheckOutcome.Failed);
}

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
