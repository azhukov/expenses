using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;
using Expenses.Domain.Extraction;

namespace Expenses.Application.Services;

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
    private static readonly string[] s_unverifiable =
    [
        ExtractedValues.Description,
        ExtractedValues.MerchantName,
        ExtractedValues.CategoryGuess,
        ExtractedValues.UnitGuess,
    ];

    private readonly ExtractionOptions _options = options ?? new ExtractionOptions();

    private readonly IReadOnlyList<IExtractionStage> _stages = [.. stages.OrderBy(stage => stage.Role)];

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

        ExtractionResult? result = null;
        ArithmeticValidationReport? report = null;

        // Every producing stage in turn, deterministic ones first, each one answering a check the
        // one before it failed rather than offering a routine second opinion: without the oracle, a
        // cascade is only a way of getting worse answers more cheaply (D20). A stage that already
        // reconciles ends it, which is what keeps a retrieved invoice from ever being second-guessed
        // by a probabilistic stage (D22).
        foreach (var stage in _stages.Where(stage => stage.Role != ExtractionStageRole.Opportunistic))
        {
            if (report?.Passed == true)
            {
                break;
            }

            // What the opportunistic stages decoded travels into the stages behind them: the
            // retrieval stage has nothing to ask the verification service without it (D22).
            var outcome = await Run(stage, image, fiscal.Carrying(known), stagesRun, cancellationToken);
            fiscal.Absorb(outcome, stage.Role);

            if (outcome.Result is not { } produced)
            {
                continue;
            }

            var producedReport = ArithmeticValidator.Validate(ExtractionArithmetic.From(produced));
            if (result is null || Better(producedReport, report))
            {
                (result, report) = (produced, producedReport);
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

        foreach (string stage in stagesRun)
        {
            result.RecordStageRun(stage);
        }

        var lowConfidence = LowConfidenceValues(result);
        bool disagrees = Disagrees(known, fiscal.Values);

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
    private static bool Better(ArithmeticValidationReport? candidate, ArithmeticValidationReport? incumbent)
        => candidate is not null && (incumbent is null || candidate.FailedCount < incumbent.FailedCount);

    private IReadOnlyList<string> LowConfidenceValues(ExtractionResult result)
    {
        var reported = result.ReportedConfidence
            .Concat(result.Candidates.SelectMany(candidate => candidate.ReportedConfidence));

        return [.. reported
            .Where(value => s_unverifiable.Contains(value.Key) && value.Value < _options.ConfidenceThreshold)
            .Select(value => value.Key)
            .Distinct()];
    }

    /// <summary>
    /// Both values are retained and the disagreement is reported; preferring one source without
    /// saying so would hide a misread receipt (D20).
    /// </summary>
    private static bool Disagrees(FiscalIdentifiers supplied, FiscalIdentifiers extracted)
        => Disagrees(supplied.Ikof, extracted.Ikof) || Disagrees(supplied.Jikr, extracted.Jikr);

    private static bool Disagrees(string? supplied, string? extracted)
        => supplied is not null && extracted is not null && !Receipt.SameFiscalIdentifier(supplied, extracted);

    /// <summary>
    /// The fiscal identity the run established, and how. A value decoded from a fiscal code or
    /// answered by the verification service is exact, so a later stage reading the same field as
    /// printed text never overwrites it (D20).
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

            // A stage that says how it knows is believed; one that does not is placed by its role,
            // which is what it has always meant.
            var source = outcome.FiscalSource
                ?? (role == ExtractionStageRole.Opportunistic
                    ? Receipt.FiscalSource.DecodedFromCode
                    : Receipt.FiscalSource.ReadAsText);

            // An estimate never displaces an exact value, and never quietly relabels where the
            // identity came from.
            if (Exactness(source) < Exactness(Source))
            {
                return;
            }

            Values = Merged(found);
            Source = source;
        }

        /// <summary>
        /// Printed text is read; a code is decoded and a service is asked. Only the first of those
        /// three can be wrong about what it saw, so it ranks below the other two.
        /// </summary>
        private static int Exactness(Receipt.FiscalSource source) => source switch
        {
            Receipt.FiscalSource.None => 0,
            Receipt.FiscalSource.ReadAsText => 1,
            _ => 2,
        };

        /// <summary>
        /// What a later stage is told: everything established so far, over whatever the upload
        /// supplied. A stage sees what the run knows, not only what the user sent with the image.
        /// </summary>
        public FiscalIdentifiers Carrying(FiscalIdentifiers supplied) => new(
            Values.Ikof ?? supplied.Ikof,
            Values.Jikr ?? supplied.Jikr,
            Values.IssuerTaxNumber ?? supplied.IssuerTaxNumber,
            Values.CreatedAt ?? supplied.CreatedAt,
            Values.Total ?? supplied.Total);

        /// <summary>
        /// Field by field, so a stage that establishes one value never erases another stage's.
        /// The JIKR in particular arrives from the verification service long after the code was
        /// decoded, and must join what the code carried rather than replace it (D24).
        /// </summary>
        private FiscalIdentifiers Merged(FiscalIdentifiers found) => new(
            found.Ikof ?? Values.Ikof,
            found.Jikr ?? Values.Jikr,
            found.IssuerTaxNumber ?? Values.IssuerTaxNumber,
            found.CreatedAt ?? Values.CreatedAt,
            found.Total ?? Values.Total);
    }
}
