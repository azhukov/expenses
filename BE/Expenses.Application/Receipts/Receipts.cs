using Expenses.Application.Abstractions;
using Expenses.Application.Errors;
using Expenses.Application.Extraction;
using Expenses.Application.Purchases;
using Expenses.Domain;
using Expenses.Domain.Extraction;

namespace Expenses.Application.Receipts;

/// <summary>
/// What capturing an image produced. Nothing here is held server-side (D12): the caller carries
/// this forward and resubmits what it needs — the temporary key, and whatever of this it wants to
/// assert unchanged or edited — when it confirms.
/// </summary>
public sealed record CaptureResult(
    Guid TempKey,
    Receipt.ExtractionState State,
    string? FailureReason,
    ExtractionResultView? Result,
    ArithmeticValidationReport? Validation,
    FiscalIdentifiers Supplied,
    FiscalIdentifiers Extracted,
    Receipt.FiscalSource FiscalSource);

/// <summary>
/// Captures an image with no purchase behind it: stores it temporarily and runs extraction
/// synchronously against it. Persists nothing to the database — not even the temporary key, which
/// exists only as the filename the temporary store gave it.
/// </summary>
public sealed class CaptureReceipt(ITemporaryReceiptStore tempStore, ExtractionCascade cascade)
{
    public async Task<CaptureResult> Execute(
        byte[] content,
        FiscalIdentifiers? suppliedFiscalIdentifiers = null,
        CancellationToken cancellationToken = default)
    {
        // Rejected before anything is written, even temporarily: the same format/size checks the
        // permanent store applies, reused rather than duplicated.
        var capture = await tempStore.Save(content, cancellationToken);

        // No purchase exists yet, so there is nothing to identify this image by beyond its own
        // bytes; the cascade's purchase identifier is purely descriptive metadata on the result.
        var image = new ReceiptImageContent(PurchaseId: 0, capture.ContentType, content);
        var supplied = suppliedFiscalIdentifiers ?? FiscalIdentifiers.None;

        var outcome = await cascade.Run(image, supplied, cancellationToken);

        return new CaptureResult(
            capture.Key,
            outcome.State,
            outcome.FailureReason,
            outcome.Result is null ? null : ExtractionResultView.Of(outcome.Result),
            outcome.Validation,
            supplied,
            outcome.Extracted,
            outcome.FiscalSource);
    }
}

public sealed class GetExtractionCandidates(
    IPurchaseRepository purchases,
    IExtractionCandidateStore candidates)
{
    public async Task<ExtractionView> Execute(long purchaseId, CancellationToken cancellationToken = default)
    {
        var purchase = await purchases.Require(purchaseId, cancellationToken);
        var receipt = purchase.RequireReceipt();

        // Candidates are transient: none held is absence rather than an extraction that produced
        // nothing, and reading never re-runs the cascade — a paid stage must not fire because
        // someone opened a page (D12).
        var result = await candidates.FindLatest(purchase.Id, cancellationToken);

        return new ExtractionView(
            ReceiptView.Of(receipt),
            result is null ? null : ExtractionResultView.Of(result),

            // Recomputed rather than stored: it is a pure function of the result (D20), and a
            // stored copy would be a second source of truth for the same fact.
            result is null ? null : ArithmeticValidator.Validate(ExtractionArithmetic.From(result)),
            CandidatesHeld: result is not null);
    }
}

/// <summary>
/// Turns candidates into the expenses of the purchase, in one transaction, with the reconciliation
/// invariant applying at that point and not before (D12, D16).
/// </summary>
public sealed class ConfirmCandidates(
    IPurchaseRepository purchases,
    IExtractionCandidateStore candidates,
    ICategoryRepository categories,
    IUnitRepository units,
    IUnitOfWork unitOfWork)
{
    public async Task<PurchaseView> Execute(
        long purchaseId,
        IReadOnlyList<ExpenseCommand>? edited = null,
        CancellationToken cancellationToken = default)
    {
        var purchase = await purchases.Require(purchaseId, cancellationToken);
        var receipt = purchase.RequireReceipt();

        var result = await candidates.FindLatest(purchase.Id, cancellationToken);

        // Confirming what a user edited is the ordinary case: candidates are a suggestion, and the
        // edits are what the user is actually asserting about the receipt (D12). Editing also
        // remains possible when the candidates themselves are gone, which is what makes their
        // transience harmless.
        var commands = edited ?? result?.Candidates.Select(AsCommand).ToList()
            ?? throw ExpensesException.For(
                ApplicationErrors.ExtractionCandidatesNotFound,
                $"No candidate lines are held for purchase {purchaseId}. Re-run extraction, or confirm edited lines.",
                ("purchaseId", purchaseId),
                ("extractionState", receipt.State.ToString()));

        if (commands.Count == 0)
        {
            throw ExpensesException.For(
                ApplicationErrors.PurchaseNoExpenses,
                "Confirming an empty set of lines would leave the purchase with no expenses.",
                ("purchaseId", purchaseId));
        }

        var lines = await ExpenseAssembly.Build(commands, categories, units, cancellationToken);

        try
        {
            purchase.ReplaceExpenses(lines);
        }
        catch (InvalidOperationException exception)
        {
            // The expenses of the purchase are left exactly as they were, and the candidates stay
            // available for the user to correct.
            var total = lines.Sum(line => line.Amount);
            throw ExpensesException.For(
                ApplicationErrors.PurchaseReconciliationMismatch,
                exception.Message,
                ("amount", purchase.Amount),
                ("expensesTotal", total));
        }

        await candidates.Discard(purchase.Id, cancellationToken);
        await unitOfWork.SaveChanges(cancellationToken);

        return PurchaseView.Of(purchase);
    }

    /// <summary>
    /// A candidate carries verbatim text and any reference it was matched to; both survive
    /// confirmation, because a match must never erase what the receipt printed (D9) — and because
    /// the candidate itself does not survive, the expense is where that text then lives.
    /// </summary>
    private static ExpenseCommand AsCommand(ExtractionCandidate candidate) => new(
        candidate.Description,
        candidate.Amount,
        candidate.Quantity,
        UnitCode: null,
        candidate.UnitPrice,
        CategoryCode: null,
        candidate.CategoryRaw,
        candidate.UnitRaw,
        candidate.ListUnitPrice,
        candidate.DiscountAmount)
    {
        MatchedCategoryId = candidate.CategoryId,
        MatchedUnitId = candidate.UnitId,
    };
}

public sealed class DiscardCandidates(
    IPurchaseRepository purchases,
    IExtractionCandidateStore candidates)
{
    public async Task Execute(long purchaseId, CancellationToken cancellationToken = default)
    {
        var purchase = await purchases.Require(purchaseId, cancellationToken);
        purchase.RequireReceipt();

        // The receipt and the purchase remain: only the suggestion is withdrawn.
        await candidates.Discard(purchase.Id, cancellationToken);
    }
}

/// <summary>
/// Removes the receipt from a purchase: the reference first, then the file — and the file only when
/// no other purchase carries the same bytes, because content addressing means they share it (D11).
/// Expenses already confirmed are untouched; they are the ledger, and the image was the source.
/// </summary>
public sealed class DeleteReceipt(
    IPurchaseRepository purchases,
    IReceiptImageStore images,
    IExtractionCandidateStore candidates,
    IUnitOfWork unitOfWork)
{
    public async Task<PurchaseView> Execute(long purchaseId, CancellationToken cancellationToken = default)
    {
        var purchase = await purchases.Require(purchaseId, cancellationToken);
        var receipt = purchase.RequireReceipt();

        purchase.DetachReceipt();
        await candidates.Discard(purchase.Id, cancellationToken);
        await unitOfWork.SaveChanges(cancellationToken);

        // After the reference is gone, so an interruption leaves an unreferenced file rather than a
        // reference to an absent one.
        if (await purchases.CountByReceiptContentHash(receipt.ContentHash, cancellationToken) == 0)
        {
            await images.Delete(receipt.StorageKey, cancellationToken);
        }

        return PurchaseView.Of(purchase);
    }
}

/// <summary>
/// Re-runs extraction for a receipt already attached to a purchase, synchronously, replacing any
/// unconfirmed candidates with the new outcome. Expenses already confirmed are never touched.
/// </summary>
public sealed class RerunExtraction(
    IPurchaseRepository purchases,
    IReceiptImageStore images,
    IExtractionCandidateStore candidates,
    ExtractionCascade cascade,
    IUnitOfWork unitOfWork)
{
    public async Task<ReceiptView> Execute(long purchaseId, CancellationToken cancellationToken = default)
    {
        var purchase = await purchases.Require(purchaseId, cancellationToken);
        var receipt = purchase.RequireReceipt();

        var bytes = await images.Read(receipt.StorageKey, cancellationToken)
            ?? throw ExpensesException.For(
                ApplicationErrors.ReceiptImageNotFound,
                $"The stored file for the receipt of purchase {purchaseId} is missing.",
                ("purchaseId", purchaseId),
                ("storageKey", receipt.StorageKey));

        var content = new ReceiptImageContent(purchase.Id, receipt.ContentType, bytes);
        var supplied = new FiscalIdentifiers(receipt.FiscalIkofSupplied, receipt.FiscalJikrSupplied);
        var outcome = await cascade.Run(content, supplied, cancellationToken);

        // Recorded before the state transition, because a disagreement between the two sources is
        // one of the things that decides the state (D20).
        receipt.RecordExtractedFiscalIdentifiers(
            outcome.Extracted.Ikof,
            outcome.Extracted.Jikr,
            outcome.FiscalSource);

        if (outcome.Result is { } result)
        {
            // Held apart from the purchase, so an unconfirmed extraction never makes the aggregate
            // invalid, and a re-run replaces the suggestion without touching confirmed data (D12).
            await candidates.Replace(purchase.Id, result, cancellationToken);
        }

        receipt.TransitionTo(outcome.State, outcome.FailureReason);
        await unitOfWork.SaveChanges(cancellationToken);

        return ReceiptView.Of(receipt);
    }
}

internal static class ReceiptLookup
{
    public static async Task<Purchase> Require(
        this IPurchaseRepository purchases,
        long purchaseId,
        CancellationToken cancellationToken) =>
        await purchases.FindById(purchaseId, cancellationToken)
        ?? throw ExpensesException.For(
            ApplicationErrors.PurchaseNotFound,
            $"There is no purchase with identifier {purchaseId}.",
            ("id", purchaseId));

    /// <summary>
    /// A receipt is addressed by its purchase and has no identity of its own (D11), so "not found"
    /// is always a statement about the purchase.
    /// </summary>
    public static Receipt RequireReceipt(this Purchase purchase) =>
        purchase.Receipt
        ?? throw ExpensesException.For(
            ApplicationErrors.ReceiptImageNotFound,
            $"Purchase {purchase.Id} has no receipt image.",
            ("purchaseId", purchase.Id));
}
