using System.ComponentModel;
using Expenses.Application.Dtos;
using Expenses.Application.Services;
using ModelContextProtocol.Server;

namespace Expenses.Mcp.Tools;

/// <summary>
/// Reading and re-running extraction for a receipt that is already stored. A receipt is addressed
/// by its purchase and has no identifier of its own (D11), and image bytes are never a tool
/// argument: uploading is HTTP-only, and this interface references what is already there.
/// </summary>
[McpServerToolType]
public sealed class ExtractionTools(ReceiptService receipts)
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
        => ExtractionToolResult.Of(await receipts.GetExtractionCandidates(purchaseId, cancellationToken));

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
        await receipts.RerunExtraction(purchaseId, cancellationToken);

        // Read back rather than reported from the re-run itself, so the shape matches get_extraction
        // exactly вЂ” the candidates it just replaced, in the same response.
        return ExtractionToolResult.Of(await receipts.GetExtractionCandidates(purchaseId, cancellationToken));
    }

    [McpServerTool(Name = "confirm_candidates")]
    [Description("""
        Turns candidate lines into the expenses of the purchase. Supply corrected lines to confirm
        those instead of what was extracted вЂ” which is also how to record lines when the candidates
        are no longer held. The amounts must sum exactly to the purchase amount, so correct them
        first if extraction misread a number.
        """)]
    public async Task<PurchaseView> ConfirmCandidates(
        [Description("The purchase whose candidate lines to confirm.")] long purchaseId,
        [Description("Corrected lines to confirm. Omit to confirm the candidates as extracted.")]
        IReadOnlyList<ExpenseArgument>? expenses = null,
        CancellationToken cancellationToken = default)
        => await receipts.ConfirmCandidates(
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
        await receipts.DiscardCandidates(purchaseId, cancellationToken);

        return $"The candidate lines for the receipt of purchase {purchaseId} were discarded. "
            + "The receipt and the purchase are unchanged.";
    }
}
