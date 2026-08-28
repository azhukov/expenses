using Expenses.Application.Abstractions;
using Expenses.Application.Extraction;
using Expenses.Domain.Extraction;

namespace Expenses.Infrastructure.Extraction;

/// <summary>
/// What the placeholder is told to produce, so that every terminal state the pipeline can reach is
/// reachable before a real engine exists (D12). A simulated outcome is configuration, not a guess
/// the engine makes about itself.
/// </summary>
internal enum PlaceholderOutcome
{
    /// <summary>Numbers that add up: the cascade validates them and the image is Extracted.</summary>
    Reconciling = 0,

    /// <summary>
    /// Numbers that do not add up. The failure is real arithmetic rather than a simulated score,
    /// which is what makes it exercise the fallback for the reason a real engine would (D20).
    /// </summary>
    NonReconciling = 1,

    /// <summary>Sound numbers with a description the engine is unsure of — the review path.</summary>
    LowConfidence = 2,

    /// <summary>No result at all.</summary>
    Failure = 3,
}

internal sealed class PlaceholderOptions
{
    public PlaceholderOutcome Outcome { get; set; } = PlaceholderOutcome.Reconciling;

    /// <summary>Reported for values arithmetic cannot decide when simulating low confidence.</summary>
    public decimal LowConfidenceValue { get; set; } = 0.35m;
}

/// <summary>
/// Stands in for a vision engine (D12). It performs no image analysis: its output is derived from
/// the content hash, so the same image always produces the same lines and a different image
/// produces different ones. Every result records the engine name and version, so placeholder rows
/// stay tellable apart from a real engine's forever.
/// </summary>
internal sealed class PlaceholderReceiptExtractor(PlaceholderOptions options, string stageName) : IReceiptExtractor
{
    public const string Engine = "placeholder";

    /// <summary>Enough lines to exercise a sum, few enough to read in a test failure.</summary>
    private const int Lines = 3;

    private const decimal TaxRatePercent = 21m;

    public string EngineName => Engine;

    public string EngineVersion => "1.0";

    public Task<ExtractionResult?> Extract(
        ReceiptImageContent image,
        CancellationToken cancellationToken = default)
    {
        if (options.Outcome == PlaceholderOutcome.Failure)
        {
            return Task.FromResult<ExtractionResult?>(null);
        }

        // Derived from the bytes rather than invented, so the same image is the same result on
        // every run and two different images are two different results.
        var seed = Seed(image.Content);
        var candidates = new List<ExtractionCandidate>(Lines);
        var total = 0m;

        for (var line = 1; line <= Lines; line++)
        {
            var amount = Amount(seed, line);
            var discount = line == 1 ? Math.Round(amount / 2m, 2) : (decimal?)null;

            candidates.Add(ExtractionCandidate.Propose(
                line,
                $"Placeholder line {line} of {seed:x8}",
                amount,
                quantity: 1m,
                unitPrice: amount,
                listUnitPrice: discount is null ? null : amount + discount,
                discountAmount: discount,
                provenance: Provenance(
                    ExtractedValues.Amount,
                    ExtractedValues.Description,
                    ExtractedValues.Quantity,
                    ExtractedValues.UnitPrice),
                reportedConfidence: ReportedConfidence()));

            total += amount;
        }

        // The failure is arithmetic a validator can prove wrong, not a score claiming doubt: a
        // simulated confidence would exercise the fallback for a reason no real engine has (D20).
        var reportedTotal = options.Outcome == PlaceholderOutcome.NonReconciling ? total + 0.50m : total;

        return Task.FromResult<ExtractionResult?>(ExtractionResult.From(
            image.PurchaseId,
            EngineName,
            EngineVersion,
            candidates,
            total: reportedTotal,
            taxRatePercent: TaxRatePercent,
            taxAmount: Math.Round(reportedTotal / (1m + (TaxRatePercent / 100m)) * (TaxRatePercent / 100m), 2),
            merchantName: $"Placeholder Merchant {seed % 97:00}",
            provenance: Provenance(
                ExtractedValues.Total,
                ExtractedValues.TaxRatePercent,
                ExtractedValues.TaxAmount,
                ExtractedValues.MerchantName),
            reportedConfidence: ReportedConfidence()));
    }

    /// <summary>
    /// Only values arithmetic cannot decide carry a reported confidence: a number the oracle checks
    /// has no use for one (D20).
    /// </summary>
    private IReadOnlyDictionary<string, decimal>? ReportedConfidence() =>
        options.Outcome == PlaceholderOutcome.LowConfidence
            ? new Dictionary<string, decimal>
            {
                [ExtractedValues.Description] = options.LowConfidenceValue,
                [ExtractedValues.MerchantName] = options.LowConfidenceValue,
            }
            : null;

    private IReadOnlyDictionary<string, string> Provenance(params string[] values) =>
        values.ToDictionary(value => value, _ => stageName);

    /// <summary>Amounts between 1.00 and about 25.00, stable for a given image and line.</summary>
    private static decimal Amount(uint seed, int line) =>
        Math.Round(1.00m + ((seed >> (line * 3)) % 2400) / 100m, 2);

    private static uint Seed(byte[] content)
    {
        // FNV-1a over the bytes: cheap, deterministic, and dependent on the whole file rather than
        // its first few bytes, so two images that differ late still differ here.
        var hash = 2166136261u;

        foreach (var value in content)
        {
            hash = (hash ^ value) * 16777619u;
        }

        return hash;
    }
}
