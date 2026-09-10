namespace Expenses.Application.Dtos;

/// <summary>
/// The threshold for values arithmetic cannot decide — descriptions, merchant names, category and
/// unit guesses (D20). Numeric fields have no threshold, because they are checked rather than
/// estimated.
/// </summary>
public sealed class ExtractionOptions
{
    public decimal ConfidenceThreshold { get; set; } = 0.70m;
}
