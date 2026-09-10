using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;
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
