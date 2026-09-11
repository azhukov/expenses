using Expenses.Domain.Entities;
using Expenses.Domain.Extraction;

namespace Expenses.Application.Dtos;

/// <summary>
/// What one extraction step produced: the lines it read, the engine that read them, the fiscal
/// identity it established, and the payload it decoded. One type for every step, so that the
/// pipeline behind them is the same code whichever step answered (D28).
///
/// It replaces three types — the old stage outcome, the portal's retrieved-invoice wrapper, and the
/// domain's extraction result (D34). A step that produced nothing at all returns null; a step that
/// established fiscal identity without reading any lines returns one of these with no candidates,
/// which is what lets the identity outlive the step that found it (D29).
///
/// Never persisted. A candidate exists to be confirmed, edited or discarded, and once confirmed it
/// is an <see cref="Expense"/>; storing it as well would be a second representation of the same
/// line for queries to mistake for the ledger (D12).
/// </summary>
public sealed record ExtractionStepResult
{
    private ExtractionStepResult(
        long purchaseId,
        string engineName,
        string engineVersion,
        IReadOnlyList<ExtractionCandidate> candidates,
        IReadOnlyList<string> stepsRun)
    {
        PurchaseId = purchaseId;
        EngineName = engineName;
        EngineVersion = engineVersion;
        Candidates = candidates;
        StepsRun = stepsRun;
    }

    /// <summary>The purchase whose receipt this was read from.</summary>
    public long PurchaseId { get; private init; }

    /// <summary>Which engine produced this, so placeholder output stays identifiable (D12).</summary>
    public string EngineName { get; private init; }

    public string EngineVersion { get; private init; }

    /// <summary>Which steps ran before and including the one that produced this.</summary>
    public IReadOnlyList<string> StepsRun { get; private init; }

    /// <summary>Empty where the step established fiscal identity but read no lines.</summary>
    public IReadOnlyList<ExtractionCandidate> Candidates { get; private init; }

    /// <summary>The advance rule: a step with no lines hands the run on to the next one (D28).</summary>
    public bool ProducedLines => Candidates.Count > 0;

    public decimal? Total { get; private init; }

    public decimal? TaxRatePercent { get; private init; }

    public decimal? TaxAmount { get; private init; }

    public string? MerchantName { get; private init; }

    public string? MerchantTaxId { get; private init; }

    /// <summary>Result-level value provenance, for values that are not per line (D20).</summary>
    public IReadOnlyDictionary<string, string> Provenance { get; private init; }
        = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, decimal> ReportedConfidence { get; private init; }
        = new Dictionary<string, decimal>();

    /// <summary>The fiscal identity this step established, where it established any.</summary>
    public FiscalIdentifiers Fiscal { get; private init; } = FiscalIdentifiers.None;

    /// <summary>
    /// How the step obtained that identity. A step that asked the verification service knows
    /// something neither the code nor printed text can tell, and the receipt records that
    /// distinction (D24).
    /// </summary>
    public Receipt.FiscalSource FiscalSource { get; private init; } = Receipt.FiscalSource.None;

    /// <summary>The fiscal QR payload the step obtained, verbatim, where it obtained one (D32).</summary>
    public string? Payload { get; private init; }

    public static ExtractionStepResult From(
        long purchaseId,
        string engineName,
        string engineVersion,
        IEnumerable<ExtractionCandidate> candidates,
        decimal? total = null,
        decimal? taxRatePercent = null,
        decimal? taxAmount = null,
        string? merchantName = null,
        string? merchantTaxId = null,
        IEnumerable<string>? stepsRun = null,
        IReadOnlyDictionary<string, string>? provenance = null,
        IReadOnlyDictionary<string, decimal>? reportedConfidence = null,
        FiscalIdentifiers? fiscal = null,
        Receipt.FiscalSource fiscalSource = Receipt.FiscalSource.None,
        string? payload = null)
    {
        if (string.IsNullOrWhiteSpace(engineName))
        {
            throw new ArgumentException("Every result records the engine that produced it.", nameof(engineName));
        }

        return new ExtractionStepResult(
            purchaseId,
            engineName.Trim(),
            engineVersion.Trim(),
            [.. candidates],
            Distinct(stepsRun))
        {
            Total = total,
            TaxRatePercent = taxRatePercent,
            TaxAmount = taxAmount,
            MerchantName = merchantName,
            MerchantTaxId = merchantTaxId,
            Provenance = provenance ?? new Dictionary<string, string>(),
            ReportedConfidence = reportedConfidence ?? new Dictionary<string, decimal>(),
            Fiscal = fiscal ?? FiscalIdentifiers.None,
            FiscalSource = fiscalSource,
            Payload = payload,
        };
    }

    /// <summary>
    /// A step that established fiscal identity, or decoded a payload, without reading any lines.
    /// The run continues past it, carrying what it found (D29).
    /// </summary>
    public static ExtractionStepResult FiscalOnly(
        string stepName,
        FiscalIdentifiers fiscal,
        Receipt.FiscalSource fiscalSource,
        string? payload = null)
        => new(purchaseId: 0, stepName, string.Empty, [], [stepName])
        {
            Fiscal = fiscal,
            FiscalSource = fiscalSource,
            Payload = payload,
        };

    /// <summary>The steps the run had taken by the time this result was produced.</summary>
    public ExtractionStepResult RunningSteps(IEnumerable<string> steps)
        => this with { StepsRun = Distinct(steps) };

    /// <summary>The payload the identity behind this result was read from (D32).</summary>
    public ExtractionStepResult WithPayload(string? payload)
        => this with { Payload = payload ?? Payload };

    /// <summary>
    /// What the run knows about the receipt's fiscal identity, merged onto what this step found.
    /// Field by field and first writer wins: with one step able to decode and one able to read text,
    /// running in that order, step order supplies the ranking an exactness ladder used to compute
    /// (D29).
    /// </summary>
    public ExtractionStepResult Carrying(
        FiscalIdentifiers known,
        Receipt.FiscalSource knownSource,
        string? knownPayload)
        => this with
        {
            Fiscal = new FiscalIdentifiers(
                known.Ikof ?? Fiscal.Ikof,
                known.Jikr ?? Fiscal.Jikr,
                known.IssuerTaxNumber ?? Fiscal.IssuerTaxNumber,
                known.CreatedAt ?? Fiscal.CreatedAt,
                known.Total ?? Fiscal.Total),
            FiscalSource = knownSource is Receipt.FiscalSource.None ? FiscalSource : knownSource,
            Payload = knownPayload ?? Payload,
        };

    private static IReadOnlyList<string> Distinct(IEnumerable<string>? steps)
        => [.. (steps ?? []).Where(step => !string.IsNullOrWhiteSpace(step)).Distinct(StringComparer.Ordinal)];
}
