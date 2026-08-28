using Expenses.Application.Abstractions;
using Expenses.Application.Errors;
using Expenses.Application.Extraction;
using Expenses.Application.Purchases;
using Expenses.Domain;
using Expenses.Domain.Extraction;

namespace Expenses.Application.Receipts;

/// <summary>
/// Attaches the one receipt a purchase may have (D11, D12). The upload does not wait for
/// extraction; the receipt is left <c>Pending</c> and queued.
/// </summary>
public sealed class AttachReceiptImage(
    IPurchaseRepository purchases,
    IReceiptImageStore images,
    IExtractionQueue queue,
    IUnitOfWork unitOfWork)
{
    public async Task<ReceiptView> Execute(
        long purchaseId,
        byte[] content,
        FiscalIdentifiers? suppliedFiscalIdentifiers = null,
        CancellationToken cancellationToken = default)
    {
        var purchase = await purchases.Require(purchaseId, cancellationToken);

        // Checked before the bytes are written, so a rejected second upload leaves nothing behind
        // and the existing receipt is untouched.
        if (purchase.Receipt is not null)
        {
            throw ExpensesException.For(
                ApplicationErrors.PurchaseImageAlreadyAttached,
                $"Purchase {purchaseId} already has a receipt image.",
                ("purchaseId", purchaseId));
        }

        // The file first, the reference second: a file nothing points at is inert, while a purchase
        // pointing at a file that was never written would not be (D11).
        var stored = await images.Save(content, cancellationToken);

        Receipt receipt;
        try
        {
            receipt = stored.AsReceipt();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw DomainErrorTranslation.Receipt(exception);
        }

        // Accepted without extraction having run, because a client that decoded them at capture
        // should not depend on the server rediscovering them (D20).
        if (suppliedFiscalIdentifiers is { IsEmpty: false } supplied)
        {
            receipt.SupplyFiscalIdentifiers(supplied.Ikof, supplied.Jikr);
        }

        try
        {
            purchase.AttachReceipt(receipt);
        }
        catch (InvalidOperationException exception)
        {
            throw DomainErrorTranslation.Receipt(exception);
        }

        await unitOfWork.SaveChanges(cancellationToken);
        await queue.Enqueue(purchase.Id, cancellationToken);

        return ReceiptView.Of(receipt);
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
/// Returns a receipt to <c>Pending</c> and queues it again, whatever terminal state it was in.
/// Confirmed expenses are never altered by a re-run.
/// </summary>
public sealed class RequeueExtraction(
    IPurchaseRepository purchases,
    IExtractionQueue queue,
    IUnitOfWork unitOfWork)
{
    public async Task<ReceiptView> Execute(long purchaseId, CancellationToken cancellationToken = default)
    {
        var purchase = await purchases.Require(purchaseId, cancellationToken);
        var receipt = purchase.RequireReceipt();

        // Pending is the only way back in, so re-running is always an explicit act. Asking for a
        // re-run of a receipt already waiting for one is absorbed rather than refused: the caller
        // wants it extracted again, and it is already going to be.
        if (receipt.State != Receipt.ExtractionState.Pending)
        {
            try
            {
                receipt.TransitionTo(Receipt.ExtractionState.Pending);
            }
            catch (InvalidOperationException exception)
            {
                throw DomainErrorTranslation.Receipt(exception);
            }
        }

        await unitOfWork.SaveChanges(cancellationToken);
        await queue.Enqueue(purchase.Id, cancellationToken);

        return ReceiptView.Of(receipt);
    }
}

/// <summary>
/// Drives one receipt through the cascade and records what it decided. Called by the background
/// drain rather than by a request (D12).
/// </summary>
public sealed class RunExtraction(
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

        try
        {
            // A receipt found still Extracting was stranded by a restart, not by another run in
            // flight: the queue is in-process, so nothing else can be extracting it (D12).
            if (receipt.State == Receipt.ExtractionState.Extracting)
            {
                receipt.TransitionTo(Receipt.ExtractionState.Pending);
            }

            receipt.TransitionTo(Receipt.ExtractionState.Extracting);
        }
        catch (InvalidOperationException exception)
        {
            throw DomainErrorTranslation.Receipt(exception);
        }

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
