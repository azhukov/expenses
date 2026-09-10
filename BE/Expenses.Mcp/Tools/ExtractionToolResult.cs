using Expenses.Application.Dtos;
using Expenses.Domain.Entities;

namespace Expenses.Mcp.Tools;

/// <summary>
/// What the ledger has made of a purchase's receipt: its state, the stages that ran, what each
/// check decided, and whether two sources of a fiscal identifier agreed.
///
/// <see cref="CandidatesHeld"/> separates "the lines are no longer held" from "extraction produced
/// none", so an assistant offers a re-run rather than reporting an empty receipt (D12).
/// </summary>
public sealed record ExtractionToolResult(
    string Summary,
    string State,
    string? FailureReason,
    string? EngineName,
    IReadOnlyList<string> StagesRun,
    IReadOnlyList<ArithmeticCheckSummary> Checks,
    string FiscalCorroboration,
    bool CandidatesHeld,
    ExtractionResultView? Result)
{
    /// <summary>
    /// The two engines whose output means something categorically different: one is the tax
    /// authority's own record of the invoice, the other is a stand-in that never looked at the image.
    /// </summary>
    private const string FiscalPortal = "fiscal-portal";

    private const string Placeholder = "placeholder";

    public static ExtractionToolResult Of(ExtractionView view)
    {
        var checks = view.Validation?.Checks.Select(ArithmeticCheckSummary.Of).ToList() ?? [];

        return new ExtractionToolResult(
            Summarise(view, checks),
            view.Receipt.State.ToString(),
            view.Receipt.FailureReason,
            view.Result?.EngineName,
            view.Result?.StagesRun ?? [],
            checks,
            view.Receipt.Corroboration.ToString(),
            view.CandidatesHeld,
            view.Result);
    }

    /// <summary>
    /// The sentence an assistant relays. It names what has to be acted on вЂ” a failed check, a
    /// disagreement between two readings of a fiscal identifier, candidates that are no longer
    /// held, or nothing at all.
    /// </summary>
    private static string Summarise(ExtractionView view, IReadOnlyList<ArithmeticCheckSummary> checks)
    {
        var failures = checks
            .Where(check => check.Outcome == nameof(CheckOutcome.Failed))
            .Select(check => check.Explanation)
            .ToList();

        string state = view.Receipt.State switch
        {
            Receipt.ExtractionState.Extracted =>
                $"Extraction read {view.Result?.Candidates.Count ?? 0} lines and the numbers add up.",
            Receipt.ExtractionState.NeedsReview =>
                $"Extraction read {view.Result?.Candidates.Count ?? 0} lines but the result needs a human eye.",
            Receipt.ExtractionState.Failed =>
                $"Extraction failed: {view.Receipt.FailureReason}",
            _ => "The extraction state is unknown.",
        };

        var reasons = new List<string>(failures);

        // Where the lines came from, in words. A retrieved invoice is the tax authority's own record
        // and a placeholder's lines are an invention, and an assistant relaying either should not
        // have to know which stage name means which (D12, D22).
        string? source = view.Result?.EngineName switch
        {
            FiscalPortal => "They are the invoice as the fiscal verification service holds it, "
                + "taken verbatim rather than read from the image.",
            Placeholder => "They came from the placeholder extraction engine, which performs no "
                + "image analysis: treat every line as provisional.",
            _ => null,
        };

        if (source is not null)
        {
            reasons.Add(source);
        }

        // Candidates are transient (D12): saying so is what stops an assistant reading the absence
        // as a receipt with nothing on it.
        if (!view.CandidatesHeld && view.Receipt.State
            is Receipt.ExtractionState.Extracted or Receipt.ExtractionState.NeedsReview)
        {
            reasons.Add("Its candidate lines are no longer held; re-run extraction to read them again.");
        }

        if (view.Receipt.Corroboration == Receipt.FiscalCorroboration.Disagreed)
        {
            reasons.Add("The fiscal identifier supplied at upload differs from the one read from the image; "
                + "both were kept.");
        }

        return reasons.Count == 0 ? state : $"{state} {string.Join(" ", reasons)}";
    }
}
