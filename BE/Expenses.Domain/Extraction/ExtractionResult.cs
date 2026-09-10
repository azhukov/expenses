namespace Expenses.Domain.Extraction;

/// <summary>
/// What one run of the cascade produced for one receipt. Held apart from the purchase so an
/// unconfirmed extraction can exist without the aggregate ever being invalid — and held only in
/// memory, because a proposal nobody accepted is not part of the ledger (D12).
/// </summary>
public sealed class ExtractionResult
{
    private readonly List<ExtractionCandidate> _candidates = [];
    private readonly List<string> _stagesRun = [];

    private ExtractionResult()
    {
        EngineName = null!;
        EngineVersion = null!;
        Provenance = null!;
    }

    /// <summary>The purchase whose receipt this was read from.</summary>
    public long PurchaseId { get; private set; }

    /// <summary>Which engine produced this, so placeholder output stays identifiable (D12).</summary>
    public string EngineName { get; private set; }

    public string EngineVersion { get; private set; }

    public IReadOnlyList<string> StagesRun => _stagesRun;

    public IReadOnlyList<ExtractionCandidate> Candidates => _candidates;

    public decimal? Total { get; private set; }

    public decimal? TaxRatePercent { get; private set; }

    public decimal? TaxAmount { get; private set; }

    public string? MerchantName { get; private set; }

    public string? MerchantTaxId { get; private set; }

    /// <summary>Result-level value provenance, for values that are not per line (D20).</summary>
    public IReadOnlyDictionary<string, string> Provenance { get; private set; }

    public IReadOnlyDictionary<string, decimal> ReportedConfidence { get; private set; } =
        new Dictionary<string, decimal>();

    public static ExtractionResult From(
        long purchaseId,
        string engineName,
        string engineVersion,
        IEnumerable<ExtractionCandidate> candidates,
        decimal? total = null,
        decimal? taxRatePercent = null,
        decimal? taxAmount = null,
        string? merchantName = null,
        string? merchantTaxId = null,
        IEnumerable<string>? stagesRun = null,
        IReadOnlyDictionary<string, string>? provenance = null,
        IReadOnlyDictionary<string, decimal>? reportedConfidence = null)
    {
        if (string.IsNullOrWhiteSpace(engineName))
        {
            throw new ArgumentException("Every result records the engine that produced it.", nameof(engineName));
        }

        var result = new ExtractionResult
        {
            PurchaseId = purchaseId,
            EngineName = engineName.Trim(),
            EngineVersion = engineVersion.Trim(),
            Total = total,
            TaxRatePercent = taxRatePercent,
            TaxAmount = taxAmount,
            MerchantName = merchantName,
            MerchantTaxId = merchantTaxId,
            Provenance = provenance ?? new Dictionary<string, string>(),
            ReportedConfidence = reportedConfidence ?? new Dictionary<string, decimal>(),
        };

        result._candidates.AddRange(candidates);

        foreach (string stage in stagesRun ?? [])
        {
            result.RecordStageRun(stage);
        }

        return result;
    }

    /// <summary>Records that a stage ran, whether or not it contributed anything.</summary>
    public void RecordStageRun(string stage)
    {
        if (string.IsNullOrWhiteSpace(stage))
        {
            throw new ArgumentException("A stage records itself by name.", nameof(stage));
        }

        if (!_stagesRun.Contains(stage))
        {
            _stagesRun.Add(stage);
        }
    }
}
