namespace Expenses.Domain.Extraction;

/// <summary>
/// A proposed expense line. Deliberately holds raw decimals with no validation applied: an
/// extraction may read numbers the domain would reject, and holding them apart from the aggregate
/// is what keeps it valid meanwhile (D12).
///
/// Never persisted. A candidate exists to be confirmed, edited or discarded, and once confirmed it
/// is an <see cref="Expense"/>; storing it as well would be a second representation of the same
/// line for queries to mistake for the ledger.
/// </summary>
public sealed class ExtractionCandidate
{
    private ExtractionCandidate()
    {
        Description = null!;
        Provenance = null!;
        ReportedConfidence = null!;
    }

    public int LineNumber { get; private set; }

    public string Description { get; private set; }

    public decimal Amount { get; private set; }

    public decimal? Quantity { get; private set; }

    public decimal? UnitPrice { get; private set; }

    public decimal? ListUnitPrice { get; private set; }

    public decimal? DiscountAmount { get; private set; }

    /// <summary>
    /// The rate this line was taxed at, where the source stated one per line. An invoice may carry
    /// several, so it cannot be inferred from the result's own rate.
    /// </summary>
    public decimal? TaxRatePercent { get; private set; }

    public string? CategoryRaw { get; private set; }

    public string? UnitRaw { get; private set; }

    public long? CategoryId { get; private set; }

    public long? UnitId { get; private set; }

    /// <summary>Value name to the stage that produced it (D20).</summary>
    public IReadOnlyDictionary<string, string> Provenance { get; private set; }

    /// <summary>
    /// Value name to a model-reported confidence, carried only for values arithmetic
    /// cannot decide — descriptions, category and unit guesses (D20).
    /// </summary>
    public IReadOnlyDictionary<string, decimal> ReportedConfidence { get; private set; }

    public static ExtractionCandidate Propose(
        int lineNumber,
        string description,
        decimal amount,
        decimal? quantity = null,
        decimal? unitPrice = null,
        decimal? listUnitPrice = null,
        decimal? discountAmount = null,
        decimal? taxRatePercent = null,
        string? categoryRaw = null,
        string? unitRaw = null,
        long? categoryId = null,
        long? unitId = null,
        IReadOnlyDictionary<string, string>? provenance = null,
        IReadOnlyDictionary<string, decimal>? reportedConfidence = null)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("A candidate line requires a description.", nameof(description));
        }

        return new ExtractionCandidate
        {
            LineNumber = lineNumber,
            Description = description.Trim(),
            Amount = amount,
            Quantity = quantity,
            UnitPrice = unitPrice,
            ListUnitPrice = listUnitPrice,
            DiscountAmount = discountAmount,
            TaxRatePercent = taxRatePercent,
            CategoryRaw = categoryRaw,
            UnitRaw = unitRaw,
            CategoryId = categoryId,
            UnitId = unitId,
            Provenance = provenance ?? new Dictionary<string, string>(),
            ReportedConfidence = reportedConfidence ?? new Dictionary<string, decimal>(),
        };
    }
}

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

        foreach (var stage in stagesRun ?? [])
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
