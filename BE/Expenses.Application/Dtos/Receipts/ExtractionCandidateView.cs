using Expenses.Domain.Extraction;

namespace Expenses.Application.Dtos;

/// <summary>
/// A proposed line. It carries raw numbers the domain would reject, which is precisely why
/// candidates are held apart from the aggregate (D12), plus the stage that produced each value
/// (D20). It has no identifier: it is addressed by its line number for as long as it is held.
/// </summary>
public sealed record ExtractionCandidateView(
    int LineNumber,
    string Description,
    decimal Amount,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal? ListUnitPrice,
    decimal? DiscountAmount,
    decimal? TaxRatePercent,
    string? CategoryRaw,
    string? UnitRaw,
    long? CategoryId,
    long? UnitId,
    IReadOnlyDictionary<string, string> Provenance,
    IReadOnlyDictionary<string, decimal> ReportedConfidence)
{
    public static ExtractionCandidateView Of(ExtractionCandidate candidate) => new(
        candidate.LineNumber,
        candidate.Description,
        candidate.Amount,
        candidate.Quantity,
        candidate.UnitPrice,
        candidate.ListUnitPrice,
        candidate.DiscountAmount,
        candidate.TaxRatePercent,
        candidate.CategoryRaw,
        candidate.UnitRaw,
        candidate.CategoryId,
        candidate.UnitId,
        candidate.Provenance,
        candidate.ReportedConfidence);
}
