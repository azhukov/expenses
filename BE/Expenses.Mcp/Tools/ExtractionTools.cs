using System.ComponentModel;
using Expenses.Application.Extraction;
using Expenses.Application.Purchases;
using Expenses.Application.Receipts;
using ModelContextProtocol.Server;

namespace Expenses.Mcp.Tools;

/// <summary>
/// One arithmetic check, in wording an assistant can act on. The outcome is a decision about the
/// numbers rather than a score, so it is reported as one (D20).
/// </summary>
public sealed record ArithmeticCheckSummary(string Check, string Outcome, string Explanation)
{
    public static ArithmeticCheckSummary Of(ArithmeticCheck check)
        => new(check.Name, check.Outcome.ToString(), check.Description);
}

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
    /// The sentence an assistant relays. It names what has to be acted on — a failed check, a
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
            Domain.Receipt.ExtractionState.Extracted =>
                $"Extraction read {view.Result?.Candidates.Count ?? 0} lines and the numbers add up.",
            Domain.Receipt.ExtractionState.NeedsReview =>
                $"Extraction read {view.Result?.Candidates.Count ?? 0} lines but the result needs a human eye.",
            Domain.Receipt.ExtractionState.Failed =>
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
            is Domain.Receipt.ExtractionState.Extracted or Domain.Receipt.ExtractionState.NeedsReview)
        {
            reasons.Add("Its candidate lines are no longer held; re-run extraction to read them again.");
        }

        if (view.Receipt.Corroboration == Domain.Receipt.FiscalCorroboration.Disagreed)
        {
            reasons.Add("The fiscal identifier supplied at upload differs from the one read from the image; "
                + "both were kept.");
        }

        return reasons.Count == 0 ? state : $"{state} {string.Join(" ", reasons)}";
    }
}

/// <summary>
/// Reading and re-running extraction for a receipt that is already stored. A receipt is addressed
/// by its purchase and has no identifier of its own (D11), and image bytes are never a tool
/// argument: uploading is HTTP-only, and this interface references what is already there.
/// </summary>
[McpServerToolType]
public sealed class ExtractionTools(
    GetExtractionCandidates getCandidates,
    RerunExtraction rerunExtraction,
    ConfirmCandidates confirmCandidates,
    DiscardCandidates discardCandidates)
{
    [McpServerTool(Name = "get_extraction")]
    [Description("""
        Reports what extraction has made of the receipt attached to a purchase: its state, the
        candidate lines it proposed, which stage produced each value, whether the numbers add up
        check by check, and whether fiscal identifiers from two sources agreed. Candidate lines are
        held only until they are confirmed, discarded or the server restarts, so a receipt can
        report a state with no lines beside it; re-run extraction to read them again. Use it before
        confirming candidates, and to explain to the user why a receipt needs review.
        """)]
    public async Task<ExtractionToolResult> GetExtraction(
        [Description("The purchase whose receipt to read.")] long purchaseId,
        CancellationToken cancellationToken = default)
        => ExtractionToolResult.Of(await getCandidates.Execute(purchaseId, cancellationToken));

    [McpServerTool(Name = "rerun_extraction")]
    [Description("""
        Runs extraction again for a purchase's receipt, synchronously, replacing any candidate lines
        that were not confirmed. Expenses already confirmed onto the purchase are left alone. Use it
        when the previous attempt failed, read the receipt badly, or its candidate lines are no
        longer held. The new state and candidates are returned in this same call.
        """)]
    public async Task<ExtractionToolResult> RerunExtraction(
        [Description("The purchase whose receipt to extract again.")] long purchaseId,
        CancellationToken cancellationToken = default)
    {
        await rerunExtraction.Execute(purchaseId, cancellationToken);

        // Read back rather than reported from the re-run itself, so the shape matches get_extraction
        // exactly — the candidates it just replaced, in the same response.
        return ExtractionToolResult.Of(await getCandidates.Execute(purchaseId, cancellationToken));
    }

    [McpServerTool(Name = "confirm_candidates")]
    [Description("""
        Turns candidate lines into the expenses of the purchase. Supply corrected lines to confirm
        those instead of what was extracted — which is also how to record lines when the candidates
        are no longer held. The amounts must sum exactly to the purchase amount, so correct them
        first if extraction misread a number.
        """)]
    public async Task<PurchaseView> ConfirmCandidates(
        [Description("The purchase whose candidate lines to confirm.")] long purchaseId,
        [Description("Corrected lines to confirm. Omit to confirm the candidates as extracted.")]
        IReadOnlyList<ExpenseArgument>? expenses = null,
        CancellationToken cancellationToken = default)
        => await confirmCandidates.Execute(
            purchaseId,
            expenses?.Select(expense => expense.ToCommand()).ToList(),
            cancellationToken);

    [McpServerTool(Name = "discard_candidates")]
    [Description("""
        Removes the candidate lines proposed for a purchase's receipt. The receipt and the purchase
        remain; only the suggestion is withdrawn.
        """)]
    public async Task<string> DiscardCandidates(
        [Description("The purchase whose candidate lines to withdraw.")] long purchaseId,
        CancellationToken cancellationToken = default)
    {
        await discardCandidates.Execute(purchaseId, cancellationToken);

        return $"The candidate lines for the receipt of purchase {purchaseId} were discarded. "
            + "The receipt and the purchase are unchanged.";
    }
}
