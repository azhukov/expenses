using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;

namespace Expenses.Application.Services;

/// <summary>
/// Extraction, as an ordered run of steps (D28). Steps run in registration order, and a step is
/// reached only when every step before it read no candidate lines. A step that reads lines ends the
/// run; a step that reads none hands forward whatever fiscal identity it established, so the step
/// behind it starts from what the run already knows (D29).
///
/// **Arithmetic is not the gate.** It used to decide whether to continue, which meant a result the
/// tax authority itself had stated could be handed to a probabilistic engine to be re-guessed
/// because its lines summed a hundredth of a unit away from its own total. Validation now runs once
/// on whatever the run produced, and only labels it — that is <see cref="ReceiptService"/>'s work,
/// not this class's (D28).
/// </summary>
public sealed class ExtractionCascade(IEnumerable<IExtractionStep> steps)
{
    private readonly IReadOnlyList<IExtractionStep> _steps = [.. steps];

    public async Task<CascadeOutcome> Run(
        ReceiptImageContent image,
        FiscalIdentifiers? supplied = null,
        string? suppliedPayload = null,
        CancellationToken cancellationToken = default)
    {
        var stepsRun = new List<string>();
        var known = supplied ?? FiscalIdentifiers.None;
        string? payload = suppliedPayload;

        // What the steps themselves established, kept apart from what the caller supplied. The two
        // are merged for the steps to read, but only this is reported as extracted: echoing the
        // caller's own reading back as a second one would make every supplied identifier look
        // corroborated by itself.
        var established = FiscalIdentifiers.None;
        var source = Receipt.FiscalSource.None;

        foreach (var step in _steps)
        {
            // Recorded whether or not it contributed, so the shape of the run is visible rather
            // than inferred (D20).
            stepsRun.Add(step.Name);

            var result = await step.Run(new ExtractionStepRequest(image, known, payload), cancellationToken);

            if (result is null)
            {
                continue;
            }

            // Field by field, first writer wins. With one step able to decode and one able to read
            // printed text, running in that order, step order supplies the ranking that an
            // exactness ladder used to compute (D29).
            (established, source, payload) = Merged(established, source, payload, result);
            known = Merge(known, established);

            if (!result.ProducedLines)
            {
                continue;
            }

            return new CascadeOutcome(
                result.RunningSteps(stepsRun).Carrying(known, source, payload),
                stepsRun,
                established,
                source,
                Payload: payload);
        }

        // The only failure there is: nothing read the receipt. A result that read lines is never a
        // failure, however its arithmetic turns out — that is a matter for review (D28).
        return new CascadeOutcome(
            null,
            stepsRun,
            established,
            source,
            "No extraction step produced a result for this image.",
            payload);
    }

    /// <summary>
    /// What the run knows after a step: each field taken from the first step to establish it, and
    /// the source and payload alongside it. An estimate never displaces an exact value because the
    /// step that decodes runs before the step that reads text, not because anything ranks them.
    /// </summary>
    private static (FiscalIdentifiers Known, Receipt.FiscalSource Source, string? Payload) Merged(
        FiscalIdentifiers known,
        Receipt.FiscalSource source,
        string? payload,
        ExtractionStepResult result)
    {
        // Identity a step read out of the payload the caller already held is not a second reading of
        // the receipt — it is the caller's own, arriving back by another route. Counting it would
        // make every supplied identifier look corroborated by itself, whether it came in with the
        // capture or off the receipt on a re-run (D31, D32).
        if (result.FiscalSource is Receipt.FiscalSource.SuppliedAtUpload)
        {
            return (known, source, payload ?? result.Payload);
        }

        // First writer wins, with one exception: the verification service outranks whatever came
        // before it. It answers with the JIKR, which is absent from the fiscal code and printed
        // nowhere the server can read it, so a receipt whose identity the service completed must
        // record that it did (D24).
        var establishedBy = result.FiscalSource is Receipt.FiscalSource.RetrievedFromService
            ? Receipt.FiscalSource.RetrievedFromService
            : source is Receipt.FiscalSource.None ? result.FiscalSource : source;

        return (Merge(known, result.Fiscal), establishedBy, payload ?? result.Payload);
    }

    /// <summary>Field by field, whatever is already known kept over what has just been found.</summary>
    private static FiscalIdentifiers Merge(FiscalIdentifiers known, FiscalIdentifiers found) => new(
        known.Ikof ?? found.Ikof,
        known.Jikr ?? found.Jikr,
        known.IssuerTaxNumber ?? found.IssuerTaxNumber,
        known.CreatedAt ?? found.CreatedAt,
        known.Total ?? found.Total);
}
