using Expenses.Application.Abstractions;
using Expenses.Domain;
using Expenses.Domain.Extraction;

namespace Expenses.Application.Extraction;

/// <summary>
/// The cascade orchestrator (D20). Free stages first, then the cheap vision tier, then arithmetic
/// validation, and the expensive tier only when validation failed. What separates
/// <c>Extracted</c> from <c>NeedsReview</c> is the arithmetic, not a score an engine reports about
/// its own work.
/// </summary>
public sealed class ExtractionCascade(IEnumerable<IExtractionStage> stages, ExtractionOptions? options = null)
{
    /// <summary>
    /// The values no arithmetic can decide, and therefore the only ones a reported confidence is
    /// consulted for (D20). Everything numeric is proved instead.
    /// </summary>
    private static readonly string[] Unverifiable =
    [
        ExtractedValues.Description,
        ExtractedValues.MerchantName,
        ExtractedValues.CategoryGuess,
        ExtractedValues.UnitGuess,
    ];

    private readonly ExtractionOptions _options = options ?? new ExtractionOptions();

    private readonly IReadOnlyList<IExtractionStage> _stages = stages
        .OrderBy(stage => stage.Role)
        .ToList();

    public async Task<CascadeOutcome> Run(
        ReceiptImageContent image,
        FiscalIdentifiers? supplied = null,
        CancellationToken cancellationToken = default)
    {
        var stagesRun = new List<string>();
        var fiscal = new FiscalTrail();
        var known = supplied ?? FiscalIdentifiers.None;

        foreach (var stage in _stages.Where(stage => stage.Role == ExtractionStageRole.Opportunistic))
        {
            // A miss is an ordinary outcome: nothing is recorded, nothing is reported, and every
            // later stage behaves exactly as it would had the stage not been configured (D20).
            var outcome = await Run(stage, image, known, stagesRun, cancellationToken);
            fiscal.Absorb(outcome, stage.Role);
        }

        var (result, report) = await RunVision(
            ExtractionStageRole.Primary,
            image,
            known,
            stagesRun,
            fiscal,
            cancellationToken);

        // The expensive tier answers a failed check rather than offering a routine second opinion:
        // without the oracle, a cascade is only a way of getting worse answers more cheaply (D20).
        if (result is null || report?.Passed != true)
        {
            var (fallback, fallbackReport) = await RunVision(
                ExtractionStageRole.Fallback,
                image,
                known,
                stagesRun,
                fiscal,
                cancellationToken);

            if (fallback is not null && (result is null || Better(fallbackReport, report)))
            {
                (result, report) = (fallback, fallbackReport);
            }
        }

        if (result is null)
        {
            return new CascadeOutcome(
                null,
                Receipt.ExtractionState.Failed,
                null,
                stagesRun,
                fiscal.Values,
                fiscal.Source,
                "No extraction stage produced a result for this image.");
        }

        foreach (var stage in stagesRun)
        {
            result.RecordStageRun(stage);
        }

        var lowConfidence = LowConfidenceValues(result);
        var disagrees = Disagrees(known, fiscal.Values);

        // Three independent reasons to put a result in front of a human, each recorded as itself
        // rather than collapsed into one number (D20).
        var state = report?.Passed == true && lowConfidence.Count == 0 && !disagrees
            ? Receipt.ExtractionState.Extracted
            : Receipt.ExtractionState.NeedsReview;

        return new CascadeOutcome(
            result,
            state,
            report,
            stagesRun,
            fiscal.Values,
            fiscal.Source,
            LowConfidenceValues: lowConfidence);
    }

    private async Task<(ExtractionResult? Result, ArithmeticValidationReport? Report)> RunVision(
        ExtractionStageRole role,
        ReceiptImageContent image,
        FiscalIdentifiers known,
        List<string> stagesRun,
        FiscalTrail fiscal,
        CancellationToken cancellationToken)
    {
        foreach (var stage in _stages.Where(stage => stage.Role == role))
        {
            var outcome = await Run(stage, image, known, stagesRun, cancellationToken);
            fiscal.Absorb(outcome, stage.Role);

            if (outcome.Result is { } produced)
            {
                return (produced, ArithmeticValidator.Validate(ExtractionArithmetic.From(produced)));
            }
        }

        return (null, null);
    }

    private static async Task<ExtractionStageOutcome> Run(
        IExtractionStage stage,
        ReceiptImageContent image,
        FiscalIdentifiers known,
        List<string> stagesRun,
        CancellationToken cancellationToken)
    {
        // Recorded whether or not it contributed, so the shape of the cost distribution is visible
        // rather than inferred (D20).
        stagesRun.Add(stage.Name);

        return await stage.Run(new ExtractionStageRequest(image, known), cancellationToken);
    }

    /// <summary>
    /// Two failed results are compared by how many checks each failed, which is the only ordering
    /// the oracle supports: fewer contradictions is closer to the receipt (D20).
    /// </summary>
    private static bool Better(ArithmeticValidationReport? candidate, ArithmeticValidationReport? incumbent) =>
        candidate is not null && (incumbent is null || candidate.FailedCount < incumbent.FailedCount);

    private IReadOnlyList<string> LowConfidenceValues(ExtractionResult result)
    {
        var reported = result.ReportedConfidence
            .Concat(result.Candidates.SelectMany(candidate => candidate.ReportedConfidence));

        return reported
            .Where(value => Unverifiable.Contains(value.Key) && value.Value < _options.ConfidenceThreshold)
            .Select(value => value.Key)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Both values are retained and the disagreement is reported; preferring one source without
    /// saying so would hide a misread receipt (D20).
    /// </summary>
    private static bool Disagrees(FiscalIdentifiers supplied, FiscalIdentifiers extracted) =>
        Disagrees(supplied.Ikof, extracted.Ikof) || Disagrees(supplied.Jikr, extracted.Jikr);

    private static bool Disagrees(string? supplied, string? extracted) =>
        supplied is not null && extracted is not null && !string.Equals(supplied, extracted, StringComparison.Ordinal);

    /// <summary>
    /// The fiscal identity the run established, and how. A value decoded from a fiscal code is
    /// exact, so a later stage reading the same field as printed text never overwrites it (D20).
    /// </summary>
    private sealed class FiscalTrail
    {
        public FiscalIdentifiers Values { get; private set; } = FiscalIdentifiers.None;

        public Receipt.FiscalSource Source { get; private set; } = Receipt.FiscalSource.None;

        public void Absorb(ExtractionStageOutcome outcome, ExtractionStageRole role)
        {
            if (outcome.Fiscal is not { IsEmpty: false } found)
            {
                return;
            }

            if (role == ExtractionStageRole.Opportunistic)
            {
                Values = new FiscalIdentifiers(found.Ikof ?? Values.Ikof, found.Jikr ?? Values.Jikr);
                Source = Receipt.FiscalSource.DecodedFromCode;
                return;
            }

            if (Source == Receipt.FiscalSource.DecodedFromCode)
            {
                return;
            }

            Values = new FiscalIdentifiers(found.Ikof ?? Values.Ikof, found.Jikr ?? Values.Jikr);
            Source = Receipt.FiscalSource.ReadAsText;
        }
    }
}
