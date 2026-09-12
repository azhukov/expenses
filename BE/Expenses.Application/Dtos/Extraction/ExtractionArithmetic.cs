namespace Expenses.Application.Dtos;

/// <summary>The numeric content of an extraction result, and nothing else.</summary>
public sealed record ExtractionArithmetic(
    IReadOnlyList<ExtractedLineAmounts> Lines,
    decimal? Total = null,
    decimal? TaxRatePercent = null,
    decimal? TaxAmount = null)
{
    /// <summary>Reduces an extraction result to the numbers the oracle can decide (D20).</summary>
    public static ExtractionArithmetic From(ExtractionStepResult result) => new(
        [.. result.Candidates.Select(ExtractedLineAmounts.From)],
        result.Total,
        result.TaxRatePercent,
        result.TaxAmount);
}
