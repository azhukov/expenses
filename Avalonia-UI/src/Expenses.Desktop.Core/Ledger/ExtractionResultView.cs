namespace Expenses.Desktop.Core.Ledger;

/// <summary>What one run of the extraction cascade produced.</summary>
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
    IReadOnlyDictionary<string, string>? Provenance,
    IReadOnlyDictionary<string, decimal>? ReportedConfidence);
