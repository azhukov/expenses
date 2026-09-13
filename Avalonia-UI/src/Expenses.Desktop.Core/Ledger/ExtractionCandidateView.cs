namespace Expenses.Desktop.Core.Ledger;

/// <summary>
/// One proposed line, beside the stage that produced each value and the confidence that stage
/// reported about it.
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
    IReadOnlyDictionary<string, string>? Provenance,
    IReadOnlyDictionary<string, decimal>? ReportedConfidence);
