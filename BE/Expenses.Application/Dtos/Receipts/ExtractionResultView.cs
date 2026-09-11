using Expenses.Domain.Extraction;

namespace Expenses.Application.Dtos;

/// <summary>
/// What one run of the cascade produced, with the engine that produced it, so placeholder output
/// stays identifiable wherever it is surfaced (D12).
/// </summary>
public sealed record ExtractionResultView(
    string EngineName,
    string EngineVersion,
    IReadOnlyList<string> StepsRun,
    IReadOnlyList<ExtractionCandidateView> Candidates,
    decimal? Total,
    decimal? TaxRatePercent,
    decimal? TaxAmount,
    string? MerchantName,
    string? MerchantTaxId,
    IReadOnlyDictionary<string, string> Provenance,
    IReadOnlyDictionary<string, decimal> ReportedConfidence)
{
    public static ExtractionResultView Of(ExtractionStepResult result) => new(
        result.EngineName,
        result.EngineVersion,
        result.StepsRun,
        [.. result.Candidates.Select(ExtractionCandidateView.Of)],
        result.Total,
        result.TaxRatePercent,
        result.TaxAmount,
        result.MerchantName,
        result.MerchantTaxId,
        result.Provenance,
        result.ReportedConfidence);
}
