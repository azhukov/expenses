using Expenses.Domain.Extraction;

namespace Expenses.Application.Dtos;

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
