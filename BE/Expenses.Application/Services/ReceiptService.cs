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
    private (Purchase.ExtractionState State, ArithmeticValidationReport? Validation,
        IReadOnlyList<string> LowConfidence) Judge(ExtractionStepResult? result)
    {
        if (result is null)
        {
            return (Purchase.ExtractionState.Failed, null, []);
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
                ? Purchase.ExtractionState.Extracted
                : Purchase.ExtractionState.NeedsReview,
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

        var outcome = await cascade.Run(image, supplied, fiscalPayload, cancellationToken: cancellationToken);
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
            fiscalPayload ?? outcome.Payload,
            await AlreadyRecorded(outcome.Extracted.Ikof ?? supplied.Ikof, cancellationToken));
    }

    /// <summary>
    /// Captures a fiscal QR payload on its own, as a client that read the code live sends it (D37).
    /// Only the steps that work from a fiscal identity run — there is no image to show the vision
    /// engine — and nothing is stored, so the result carries no temporary key. A payload the service
    /// returned no invoice for is Failed, with what the payload states still reported, so the caller
    /// can decide whether to send it again beside a photograph.
    /// </summary>
    public async Task<CaptureResult> CaptureFiscal(string payload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw ExpensesException.For(
                ApplicationErrors.ReceiptFiscalPayloadRequired,
                "A fiscal capture requires the payload the receipt's QR code carries.");
        }

        // Parsed once, by the one parser that reads this format (D30). An unrecognised payload is
        // an ordinary capture carrying no identifiers, not an error.
        var supplied = FiscalIdentity.From(payload);

        var outcome = await cascade.Run(image: null, supplied, payload, cancellationToken: cancellationToken);
        var (state, validation, _) = Judge(outcome.Result);

        return new CaptureResult(
            TempKey: null,
            state,
            outcome.FailureReason,
            outcome.Result is null ? null : ExtractionResultView.Of(outcome.Result),
            validation,
            supplied,
            outcome.Extracted,
            outcome.FiscalSource,
            payload,
            await AlreadyRecorded(outcome.Extracted.Ikof ?? supplied.Ikof, cancellationToken));
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
        var state = purchase.RequireExtraction();

        // Candidates are transient: none held is absence rather than an extraction that produced
        // nothing, and reading never re-runs the cascade вЂ” a paid stage must not fire because
        // someone opened a page (D12).
        var result = await candidates.FindLatest(purchase.Id, cancellationToken);

        return new ExtractionView(
            ReceiptView.Of(purchase, state),
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
        var state = purchase.RequireExtraction();

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
                ("extractionState", state.ToString()));

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
        purchase.RequireExtraction();

        // The receipt and the purchase remain: only the suggestion is withdrawn.
        await candidates.Discard(purchase.Id, cancellationToken);
    }

    /// <summary>
    /// Re-runs extraction for a purchase's receipt, synchronously, replacing any unconfirmed
    /// candidates with the new outcome. Expenses already confirmed are never touched. A purchase read
    /// from its fiscal code alone has no image, and only the steps that need none run (D37).
    /// </summary>
    public async Task<ReceiptView> RerunExtraction(long purchaseId, CancellationToken cancellationToken = default)
    {
        var purchase = await purchases.Require(purchaseId, cancellationToken);
        purchase.RequireExtraction();

        var content = purchase.Receipt is { } receipt
            ? await Content(purchase.Id, receipt, cancellationToken)
            : null;

        // The payload the purchase already holds, rather than the photograph it came from. Decoding
        // a stored image reads one symbol in three, so re-running from the image would lose a
        // reading the purchase already has — every time (D32).
        var held = purchase.Fiscal;
        string? payload = held?.FiscalPayload;
        var supplied = payload is null
            ? new FiscalIdentifiers(held?.FiscalIkofSupplied, held?.FiscalJikrSupplied)
            : FiscalIdentity.From(payload);

        var outcome = await cascade.Run(content, supplied, payload, purchase.Id, cancellationToken);
        var (state, _, _) = Judge(outcome.Result);

        // A purchase read from an image may learn its invoice only now, from a code this run
        // decoded; one that already carries an invoice records onto it (D35).
        var fiscal = held ?? FiscalInvoice.Create();

        fiscal.RecordExtractedIdentifiers(
            outcome.Extracted.Ikof,
            outcome.Extracted.Jikr,
            outcome.FiscalSource);

        // A payload decoded by this run is retained, so the next one need not decode again.
        fiscal.RecordPayload(outcome.Payload, outcome.FiscalSource);

        if (held is null)
        {
            purchase.AttachFiscalInvoice(fiscal);
        }

        if (outcome.Result is { } result)
        {
            // Held apart from the purchase, so an unconfirmed extraction never makes the aggregate
            // invalid, and a re-run replaces the suggestion without touching confirmed data (D12).
            await candidates.Replace(purchase.Id, result, cancellationToken);
        }

        purchase.TransitionExtraction(state, outcome.FailureReason);
        await unitOfWork.SaveChanges(cancellationToken);

        return ReceiptView.Of(purchase, state);
    }

    /// <summary>
    /// The purchase already recorded against the invoice a capture established, whichever way it was
    /// established — supplied, decoded or answered by the service (D39).
    /// </summary>
    private async Task<AlreadyRecordedInvoice?> AlreadyRecorded(string? ikof, CancellationToken cancellationToken)
        => string.IsNullOrWhiteSpace(ikof)
            || await purchases.FindLatestByInvoiceCode(ikof.Trim(), cancellationToken) is not { } earlier
            ? null
            : new AlreadyRecordedInvoice(earlier.Id, earlier.OccurredAt);

    /// <summary>The stored bytes of a purchase's receipt image, or the reason there are none.</summary>
    private async Task<ReceiptImageContent> Content(
        long purchaseId,
        Receipt receipt,
        CancellationToken cancellationToken)
    {
        byte[] bytes = await images.Read(receipt.StorageKey, cancellationToken)
            ?? throw ExpensesException.For(
                ApplicationErrors.ReceiptImageNotFound,
                $"The stored file for the receipt of purchase {purchaseId} is missing.",
                ("purchaseId", purchaseId),
                ("storageKey", receipt.StorageKey));

        return new ReceiptImageContent(purchaseId, receipt.ContentType, bytes);
    }

    /// <summary>
    /// A candidate carries verbatim text and any reference it was matched to; both survive
    /// confirmation, because a match must never erase what the receipt printed (D9) вЂ” and because
    /// the candidate itself does not survive, the expense is where that text then lives.
    ///
    /// No unit code is passed and none is invented: the only unit a candidate has is the one a
    /// stage matched. Where a stage matched none, confirming without edited lines is refused
    /// rather than defaulted to a count unit, which would state something the receipt did not.
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
