using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;

namespace Expenses.Infrastructure.Extraction;

/// <summary>
/// The probabilistic step, and the last one (D28). One tier, not two: both tiers were configured
/// instances of the same placeholder, so the escalation between them never chose between two real
/// engines, and at personal-ledger volume the ladder saved single-digit dollars a year.
///
/// It is handed the fiscal identity the run established, so that a known-true total and issuer
/// identity reach the engine even where the verification service could not be asked (D23).
/// </summary>
internal sealed class VisionStep(IReceiptExtractor extractor) : IExtractionStep
{
    public const string StepName = "vision";

    public string Name => StepName;

    public bool ReadsImage => true;

    public Task<ExtractionStepResult?> Run(
        ExtractionStepRequest request,
        CancellationToken cancellationToken = default)
        => extractor.Extract(
            request.Image ?? throw new InvalidOperationException("The vision step was reached with no image."),
            request.Fiscal,
            cancellationToken);
}
