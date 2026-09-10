using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;

namespace Expenses.Infrastructure.Extraction;

/// <summary>
/// A vision tier (D20). Stages 2 and 4 are two configured instances of this over the same
/// placeholder in this change, and two instances over a real engine later; the cascade does not
/// change when they become real, which is the point of building it now.
/// </summary>
internal sealed class VisionStage(
    IReceiptExtractor extractor,
    string name,
    ExtractionStageRole role) : IExtractionStage
{
    public const string CheapTier = "vision-cheap";

    public const string ExpensiveTier = "vision-expensive";

    public string Name => name;

    public ExtractionStageRole Role => role;

    public async Task<ExtractionStageOutcome> Run(
        ExtractionStageRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await extractor.Extract(request.Image, cancellationToken);

        return result is null ? ExtractionStageOutcome.Nothing : new ExtractionStageOutcome(result);
    }
}
