using Expenses.Application.Dtos;
using Expenses.Application.Errors;
using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;
using Expenses.Domain.Extraction;

namespace Expenses.Application.Services;

/// <summary>
/// Receipts and the extraction that reads them: capturing an image with no purchase behind it,
/// reading and confirming or discarding the candidates extraction proposed, and removing or
/// re-running extraction for a receipt already attached to a purchase.
/// </summary>
public sealed class ReceiptService(
    IPurchaseRepository purchases,
    IReceiptImageStore images,
    ITemporaryReceiptStore tempStore,
    IExtractionCandidateStore candidates,
    ICategoryRepository categories,
    IUnitRepository units,
    ExtractionCascade cascade,
    IUnitOfWork unitOfWork,
    ExtractionOptions? options = null)
{
    /// <summary>
    /// The values no arithmetic can decide, and therefore the only ones a reported confidence is
    /// consulted for (D20). Everything numeric is proved instead.
    /// </summary>
    private static readonly string[] s_unverifiable =
    [
        ExtractedValues.Description,
        ExtractedValues.MerchantName,
        ExtractedValues.CategoryGuess,
        ExtractedValues.UnitGuess,
    ];

    private readonly ExtractionOptions _options = options ?? new ExtractionOptions();

    /// <summary>
    /// What a run of extraction amounts to, decided here rather than inside the pipeline: the
    /// arithmetic oracle is a pure function of a result, so it is applied once to whatever the run
    /// produced and only labels it (D28). Two independent reasons put a result in front of a
    /// person, and each is recorded as itself rather than collapsed into one number (D20).
    /// </summary>
    private (Receipt.ExtractionState State, ArithmeticValidationReport? Validation,
        IReadOnlyList<string> LowConfidence) Judge(ExtractionStepResult? result)
    {
        if (result is null)
        {
            return (Receipt.ExtractionState.Failed, null, []);
        }

        var validation = ArithmeticValidator.Validate(ExtractionArithmetic.From(result));

        var reported = result.ReportedConfidence
            .Concat(result.Candidates.SelectMany(candidate => candidate.ReportedConfidence));

        IReadOnlyList<string> lowConfidence =
        [
            .. reported
                .Where(value => s_unverifiable.Contains(value.Key)
                    && value.Value < _options.ConfidenceThreshold)
                .Select(value => value.Key)
                .Distinct(StringComparer.Ordinal),
        ];

        return (
            validation.Passed && lowConfidence.Count == 0
                ? Receipt.ExtractionState.Extracted
                : Receipt.ExtractionState.NeedsReview,
            validation,
            lowConfidence);
    }

    /// <summary>
    /// Captures an image with no purchase behind it: stores it temporarily and runs extraction
    /// synchronously against it. Persists nothing to the database вЂ” not even the temporary key, which
    /// exists only as the filename the temporary store gave it.
    /// </summary>
    public async Task<CaptureResult> Capture(
        byte[] content,
        FiscalIdentifiers? suppliedFiscalIdentifiers = null,
        string? fiscalPayload = null,
        CancellationToken cancellationToken = default)
    {
        // Rejected before anything is written, even temporarily: the same format/size checks the
        // permanent store applies, reused rather than duplicated.
        var capture = await tempStore.Save(content, cancellationToken);

        // No purchase exists yet, so there is nothing to identify this image by beyond its own
        // bytes; the cascade's purchase identifier is purely descriptive metadata on the result.
        var image = new ReceiptImageContent(PurchaseId: 0, capture.ContentType, content);
        var supplied = suppliedFiscalIdentifiers ?? FiscalIdentifiers.None;

        var outcome = await cascade.Run(image, supplied, fiscalPayload, cancellationToken);
        var (state, validation, _) = Judge(outcome.Result);

        return new CaptureResult(
            capture.Key,
            state,
            outcome.FailureReason,
            outcome.Result is null ? null : ExtractionResultView.Of(outcome.Result),
            validation,
            supplied,
            outcome.Extracted,
            outcome.FiscalSource,

            // Handed back so the caller can resubmit it at confirmation, which is the only point at
            // which a receipt exists to retain it (D32). Nothing is held server-side meanwhile.
            fiscalPayload ?? outcome.Payload);
    }

    /// <summary>
    /// Removes the receipt from a purchase: the reference first, then the file вЂ” and the file only when
    /// no other purchase carries the same bytes, because content addressing means they share it (D11).
    /// Expenses already confirmed are untouched; they are the ledger, and the image was the source.
    /// </summary>
    public async Task<PurchaseView> Delete(long purchaseId, CancellationToken cancellationToken = default)
    {
        var purchase = await purchases.Require(purchaseId, cancellationToken);
        var receipt = purchase.RequireReceipt();

        purchase.DetachReceipt();
        await candidates.Discard(purchase.Id, cancellationToken);
        await unitOfWork.SaveChanges(cancellationToken);

        // After the reference is gone, so an interruption leaves an unreferenced file rather than a
        // reference to an absent one.
        if (await purchases.CountByReceiptStorageKey(receipt.StorageKey, cancellationToken) == 0)
        {
            await images.Delete(receipt.StorageKey, cancellationToken);
        }

        return PurchaseView.Of(purchase);
    }

    public async Task<ExtractionView> GetExtractionCandidates(
        long purchaseId,
        CancellationToken cancellationToken = default)
    {
        var purchase = await purchases.Require(purchaseId, cancellationToken);
        var receipt = purchase.RequireReceipt();

        // Candidates are transient: none held is absence rather than an extraction that produced
        // nothing, and reading never re-runs the cascade вЂ” a paid stage must not fire because
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

    /// <summary>
    /// Turns candidates into the expenses of the purchase, in one transaction, with the reconciliation
    /// invariant applying at that point and not before (D12, D16).
    /// </summary>
    public async Task<PurchaseView> ConfirmCandidates(
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
            decimal total = lines.Sum(line => line.Amount);
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

    public async Task DiscardCandidates(long purchaseId, CancellationToken cancellationToken = default)
    {
        var purchase = await purchases.Require(purchaseId, cancellationToken);
        purchase.RequireReceipt();

        // The receipt and the purchase remain: only the suggestion is withdrawn.
        await candidates.Discard(purchase.Id, cancellationToken);
    }

    /// <summary>
    /// Re-runs extraction for a receipt already attached to a purchase, synchronously, replacing any
    /// unconfirmed candidates with the new outcome. Expenses already confirmed are never touched.
    /// </summary>
    public async Task<ReceiptView> RerunExtraction(long purchaseId, CancellationToken cancellationToken = default)
    {
        var purchase = await purchases.Require(purchaseId, cancellationToken);
        var receipt = purchase.RequireReceipt();

        byte[] bytes = await images.Read(receipt.StorageKey, cancellationToken)
            ?? throw ExpensesException.For(
                ApplicationErrors.ReceiptImageNotFound,
                $"The stored file for the receipt of purchase {purchaseId} is missing.",
                ("purchaseId", purchaseId),
                ("storageKey", receipt.StorageKey));

        var content = new ReceiptImageContent(purchase.Id, receipt.ContentType, bytes);

        // The payload the receipt already holds, rather than the photograph it came from. Decoding
        // a stored image reads one symbol in three, so re-running from the image would lose a
        // reading the receipt already has — every time (D32).
        string? payload = receipt.FiscalPayload;
        var supplied = payload is null
            ? new FiscalIdentifiers(receipt.FiscalIkofSupplied, receipt.FiscalJikrSupplied)
            : FiscalIdentity.From(payload);

        var outcome = await cascade.Run(content, supplied, payload, cancellationToken);
        var (state, _, _) = Judge(outcome.Result);

        receipt.RecordExtractedFiscalIdentifiers(
            outcome.Extracted.Ikof,
            outcome.Extracted.Jikr,
            outcome.FiscalSource);

        // A payload decoded by this run is retained, so the next one need not decode again.
        receipt.RecordFiscalPayload(outcome.Payload, outcome.FiscalSource);

        if (outcome.Result is { } result)
        {
            // Held apart from the purchase, so an unconfirmed extraction never makes the aggregate
            // invalid, and a re-run replaces the suggestion without touching confirmed data (D12).
            await candidates.Replace(purchase.Id, result, cancellationToken);
        }

        receipt.TransitionTo(state, outcome.FailureReason);
        await unitOfWork.SaveChanges(cancellationToken);

        return ReceiptView.Of(receipt);
    }

    /// <summary>
    /// A candidate carries verbatim text and any reference it was matched to; both survive
    /// confirmation, because a match must never erase what the receipt printed (D9) вЂ” and because
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
