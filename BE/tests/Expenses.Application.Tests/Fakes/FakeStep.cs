using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;

namespace Expenses.Application.Tests.Fakes;

/// <summary>
/// A step that returns what the test told it to and counts its runs, so "the vision step does not
/// run behind a retrieved invoice" is an assertion rather than an inference (D28).
/// </summary>
internal sealed class FakeStep(
    string name,
    Func<ExtractionStepRequest, ExtractionStepResult?> behaviour) : IExtractionStep
{
    public string Name { get; } = name;

    public int Runs { get; private set; }

    /// <summary>What the step was given when it last ran, so threading can be asserted (D29).</summary>
    public ExtractionStepRequest? LastRequest { get; private set; }

    public static FakeStep Producing(string name, ExtractionStepResult result)
        => new(name, _ => result);

    public static FakeStep Silent(string name) => new(name, _ => null);

    /// <summary>
    /// The deterministic step answering with a whole invoice and the identifier only the
    /// verification service knows, recorded as having come from there (D24).
    /// </summary>
    public static FakeStep Retrieving(string name, ExtractionStepResult result, FiscalIdentifiers identifiers)
        => new(name, _ => result.Carrying(identifiers, Receipt.FiscalSource.RetrievedFromService, null));

    /// <summary>
    /// A step that established fiscal identity but read no lines: the run continues past it,
    /// carrying what it found (D29).
    /// </summary>
    public static FakeStep FiscalOnly(string name, FiscalIdentifiers identifiers, string? payload = null)
        => new(name, _ => ExtractionStepResult.FiscalOnly(
            name,
            identifiers,
            Receipt.FiscalSource.DecodedFromCode,
            payload));

    public Task<ExtractionStepResult?> Run(
        ExtractionStepRequest request,
        CancellationToken cancellationToken = default)
    {
        Runs++;
        LastRequest = request;

        return Task.FromResult(behaviour(request));
    }
}
