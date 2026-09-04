using Expenses.Application.Extraction;
using Expenses.Domain;
using Expenses.Domain.Extraction;

namespace Expenses.Application.Receipts;

/// <summary>
/// A receipt as it is read back. It has no identifier of its own: a receipt is addressed by the
/// purchase that carries it (D11). The fiscal identifiers are carried per source with the derived
/// corroboration beside them, because a disagreement is reported rather than resolved (D20).
/// </summary>
public sealed record ReceiptView(
    string ContentType,
    long SizeInBytes,
    Receipt.ExtractionState State,
    string? FailureReason,
    string? FiscalIkofSupplied,
    string? FiscalIkofExtracted,
    string? FiscalJikrSupplied,
    string? FiscalJikrExtracted,
    Receipt.FiscalSource FiscalExtractedSource,
    Receipt.FiscalCorroboration Corroboration)
{
    public static ReceiptView Of(Receipt receipt) => new(
        receipt.ContentType,
        receipt.SizeInBytes,
        receipt.State,
        receipt.FailureReason,
        receipt.FiscalIkofSupplied,
        receipt.FiscalIkofExtracted,
        receipt.FiscalJikrSupplied,
        receipt.FiscalJikrExtracted,
        receipt.FiscalExtractedSource,
        receipt.Corroboration);
}

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

/// <summary>
/// What one run of the cascade produced, with the engine that produced it, so placeholder output
/// stays identifiable wherever it is surfaced (D12).
/// </summary>
public sealed record ExtractionResultView(
    string EngineName,
    string EngineVersion,
    IReadOnlyList<string> StagesRun,
    IReadOnlyList<ExtractionCandidateView> Candidates,
    decimal? Total,
    decimal? TaxRatePercent,
    decimal? TaxAmount,
    string? MerchantName,
    string? MerchantTaxId,
    IReadOnlyDictionary<string, string> Provenance,
    IReadOnlyDictionary<string, decimal> ReportedConfidence)
{
    public static ExtractionResultView Of(ExtractionResult result) => new(
        result.EngineName,
        result.EngineVersion,
        result.StagesRun,
        result.Candidates.Select(ExtractionCandidateView.Of).ToList(),
        result.Total,
        result.TaxRatePercent,
        result.TaxAmount,
        result.MerchantName,
        result.MerchantTaxId,
        result.Provenance,
        result.ReportedConfidence);
}

/// <summary>
/// A receipt together with whatever the cascade has made of it — including the arithmetic report,
/// which is what a reviewer needs in order to see why it needs reviewing (D20).
///
/// <see cref="CandidatesHeld"/> tells "no candidates are held any more" apart from "extraction
/// produced no lines": candidates do not survive a restart, and that absence is not a failure
/// (D12).
/// </summary>
public sealed record ExtractionView(
    ReceiptView Receipt,
    ExtractionResultView? Result,
    ArithmeticValidationReport? Validation,
    bool CandidatesHeld);
