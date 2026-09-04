using Expenses.Application.Extraction;
using Expenses.Domain;
using Expenses.Domain.Extraction;

namespace Expenses.Application.Tests.Fakes;

/// <summary>
/// A stage that returns what the test told it to and counts its runs, so "the expensive stage does
/// not run when the cheap one added up" is an assertion rather than an inference (D20).
/// </summary>
internal sealed class FakeStage(
    string name,
    ExtractionStageRole role,
    Func<ExtractionStageRequest, ExtractionStageOutcome> behaviour) : IExtractionStage
{
    public string Name { get; } = name;

    public ExtractionStageRole Role { get; } = role;

    public int Runs { get; private set; }

    public static FakeStage Producing(string name, ExtractionStageRole role, ExtractionResult result)
        => new(name, role, _ => new ExtractionStageOutcome(result));

    public static FakeStage Silent(string name, ExtractionStageRole role)
        => new(name, role, _ => ExtractionStageOutcome.Nothing);

    /// <summary>
    /// The deterministic stage that answers with a whole invoice and the identifier only the
    /// verification service knows, recorded as having come from there (D24).
    /// </summary>
    public static FakeStage Retrieving(string name, ExtractionResult result, FiscalIdentifiers identifiers)
        => new(name, ExtractionStageRole.Primary, _ => new ExtractionStageOutcome(
            result,
            identifiers,
            Receipt.FiscalSource.RetrievedFromService));

    public static FakeStage Decoding(string name, FiscalIdentifiers identifiers)
        => new(name, ExtractionStageRole.Opportunistic, _ => new ExtractionStageOutcome(Fiscal: identifiers));

    public Task<ExtractionStageOutcome> Run(
        ExtractionStageRequest request,
        CancellationToken cancellationToken = default)
    {
        Runs++;
        return Task.FromResult(behaviour(request));
    }
}

/// <summary>Extraction results shaped for the arithmetic oracle, built the way a receipt reads.</summary>
internal static class Results
{
    /// <summary>The worked example from D20: two discounted lines that reconcile with the total.</summary>
    public static ExtractionResult Reconciling(
        string stage,
        long receiptImageId = 1,
        string engine = "placeholder",
        IReadOnlyDictionary<string, decimal>? reportedConfidence = null)
        => ExtractionResult.From(
            receiptImageId,
            engine,
            "1.0",
            [
                ExtractionCandidate.Propose(
                    1,
                    "Sladoled Milka Mini Sticks",
                    4.49m,
                    listUnitPrice: 8.50m,
                    discountAmount: 4.01m,
                    provenance: new Dictionary<string, string> { ["amount"] = stage }),
                ExtractionCandidate.Propose(
                    2,
                    "Cokolada",
                    3.99m,
                    listUnitPrice: 7.50m,
                    discountAmount: 3.51m,
                    provenance: new Dictionary<string, string> { ["amount"] = stage }),
            ],
            total: 8.48m,
            taxRatePercent: 21m,
            taxAmount: 1.47m,
            provenance: new Dictionary<string, string> { ["total"] = stage },
            reportedConfidence: reportedConfidence);

    /// <summary>
    /// A result whose lines do not sum to its total. <paramref name="alsoBreakDiscount"/> breaks a
    /// second check as well, which is what makes two failed results comparable (D20).
    /// </summary>
    public static ExtractionResult Failing(
        string stage,
        bool alsoBreakDiscount = false,
        long receiptImageId = 1)
        => ExtractionResult.From(
            receiptImageId,
            "placeholder",
            "1.0",
            [
                ExtractionCandidate.Propose(
                    1,
                    "Sladoled Milka Mini Sticks",
                    4.49m,
                    listUnitPrice: 8.50m,
                    discountAmount: alsoBreakDiscount ? 3.01m : 4.01m,
                    provenance: new Dictionary<string, string> { ["amount"] = stage }),
                ExtractionCandidate.Propose(2, "Cokolada", 3.99m),
            ],
            total: 8.98m,
            provenance: new Dictionary<string, string> { ["total"] = stage });
}
