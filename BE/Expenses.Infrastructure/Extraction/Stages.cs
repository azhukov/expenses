using Expenses.Application.Abstractions;
using Expenses.Application.Extraction;
using Microsoft.Extensions.Logging;

namespace Expenses.Infrastructure.Extraction;

/// <summary>
/// Stage 1 of the cascade (D20): decode a fiscal code from the stored image. It is opportunistic by
/// construction — measured to miss on photographed thermal receipts across some three hundred
/// preprocessing combinations — so a miss, and a decoder that throws, are both ordinary outcomes
/// that leave every later stage behaving exactly as it would have.
/// </summary>
internal sealed class FiscalDecodeStage(IFiscalCodeDecoder decoder, ILogger<FiscalDecodeStage> logger)
    : IExtractionStage
{
    public const string StageName = "fiscal-qr";

    public string Name => StageName;

    public ExtractionStageRole Role => ExtractionStageRole.Opportunistic;

    public async Task<ExtractionStageOutcome> Run(
        ExtractionStageRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var decoded = await decoder.Decode(request.Image, cancellationToken);

            return decoded is { IsEmpty: false }
                ? new ExtractionStageOutcome(Fiscal: decoded)
                : ExtractionStageOutcome.Nothing;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A decoder that throws is a decoder that missed. Nothing depends on this stage
            // hitting, so a failure here must not become a failure of the extraction, an error the
            // user sees, or a reason for a later stage to behave differently (D20).
            logger.LogDebug(
                exception,
                "Fiscal code decoding failed for the receipt of purchase {PurchaseId}; continuing without it.",
                request.Image.PurchaseId);

            return ExtractionStageOutcome.Nothing;
        }
    }
}

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
