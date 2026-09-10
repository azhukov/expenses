using System.Globalization;
using Expenses.Application.Dtos;
using Expenses.Application.Errors;
using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;

namespace Expenses.Application.Services;

/// <summary>Recording and reading purchases (D1).</summary>
public sealed class PurchaseService(
    IPurchaseRepository purchases,
    ICategoryRepository categories,
    IUnitRepository units,
    MerchantService merchants,
    ITemporaryReceiptStore tempStore,
    IReceiptImageStore images,
    IUnitOfWork unitOfWork)
{
    /// <summary>The page size a caller gets when it asks for none.</summary>
    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 200;

    /// <summary>
    /// Records a purchase, absorbing a repeated submission of the same request (D3, D4). Confirming a
    /// capture is folded into the same call: a purchase is created whole, with its receipt attached
    /// from the moment it exists, never in a separate step afterward. <paramref name="occurredAt"/>
    /// may be omitted only when confirming a capture whose fiscal QR decoded an invoice creation
    /// timestamp: a manually recorded purchase always states its own date (D5).
    /// </summary>
    public async Task<RecordPurchaseResult> Record(
        DateTime? occurredAt,
        decimal amount,
        IReadOnlyList<ExpenseCommand> expenses,
        MerchantCommand? merchant = null,
        CapturedReceiptCommand? capture = null,
        CancellationToken cancellationToken = default)
    {
        if (expenses.Count == 0)
        {
            throw ExpensesException.For(
                ApplicationErrors.PurchaseNoExpenses,
                "A purchase requires at least one expense.");
        }

        var resolvedOccurredAt = ResolveOccurrence(occurredAt, capture);

        // The fast path: one round trip, a clean result, and no exception used as control flow.
        // The unique index the catch below relies on is what makes the guarantee true when two
        // requests race (D4).
        if (await purchases.FindByOccurrenceAndAmount(resolvedOccurredAt, amount, cancellationToken) is { } already)
        {
            return AlreadyRecorded(already);
        }

        var lines = await ExpenseAssembly.Build(expenses, categories, units, cancellationToken);
        Reconcile(amount, lines);

        // Resolved only once the submission is known to be new, so a repeated request cannot add a
        // merchant as a side effect of being ignored.
        var resolvedMerchant = merchant is { } named
            ? await merchants.Resolve(named.Text, named.TaxId, cancellationToken)
            : null;

        // Promoted only once every rejection that must leave the capture untouched has already
        // happened: a reconciliation failure above never touches the temporary store (D11, D12).
        var receipt = capture is { } toPromote
            ? await Promote(toPromote, cancellationToken)
            : null;

        Purchase purchase;
        try
        {
            purchase = Purchase.Record(
                resolvedOccurredAt,
                amount,
                lines,
                resolvedMerchant?.Merchant.Id,
                merchant?.Text,
                receipt);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw DomainErrorTranslation.Purchase(exception);
        }

        try
        {
            await purchases.Add(purchase, cancellationToken);
            await unitOfWork.SaveChanges(cancellationToken);
        }
        catch (DuplicatePurchaseException)
        {
            // Another writer committed the same pair between the query above and this insert. The
            // winner is what both callers asked for, so re-query and hand it back as a success. The
            // capture, if any, was already promoted and is discarded below regardless of who won:
            // its bytes are safely in the permanent store either way.
            var winner = await purchases.FindByOccurrenceAndAmount(resolvedOccurredAt, amount, cancellationToken)
                ?? throw ExpensesException.For(
                    ApplicationErrors.PurchaseNotFound,
                    "A purchase with this occurrence and amount was recorded concurrently but cannot be read back.",
                    ("occurredAt", resolvedOccurredAt),
                    ("amount", amount));

            if (capture is { } raced)
            {
                await tempStore.Delete(raced.TempKey, cancellationToken);
            }

            return AlreadyRecorded(winner);
        }

        if (capture is { } confirmed)
        {
            await tempStore.Delete(confirmed.TempKey, cancellationToken);
        }

        return new RecordPurchaseResult(
            PurchaseView.Of(purchase),
            AlreadyRecorded: false,
            resolvedMerchant is null ? null : MerchantView.Of(resolvedMerchant.Merchant),
            resolvedMerchant?.NewlyAdded ?? false);
    }

    /// <summary>Returns one purchase with its expense lines, merchant and derived saving.</summary>
    public async Task<PurchaseView> Get(long id, CancellationToken cancellationToken = default)
    {
        var purchase = await purchases.FindById(id, cancellationToken)
            ?? throw ExpensesException.For(
                ApplicationErrors.PurchaseNotFound,
                $"There is no purchase with identifier {id}.",
                ("id", id));

        // Reading a purchase answers "has the receipt been read yet" in the same read: the receipt
        // is columns on the purchase, not a row to join (D11).
        return PurchaseView.Of(purchase);
    }

    /// <summary>Date-range filtering, most recent first, paged. Both bounds are inclusive dates.</summary>
    public async Task<IReadOnlyList<PurchaseView>> List(
        DateOnly? from = null,
        DateOnly? to = null,
        int skip = 0,
        int? take = null,
        CancellationToken cancellationToken = default)
    {
        int resolvedTake = take ?? DefaultPageSize;

        if (resolvedTake is < 1 || resolvedTake > MaxPageSize)
        {
            throw ExpensesException.For(
                ApplicationErrors.ListingPageSizeInvalid,
                $"A page holds between 1 and {MaxPageSize} purchases.",
                ("take", resolvedTake));
        }

        if (skip < 0)
        {
            throw ExpensesException.For(
                ApplicationErrors.ListingPageSizeInvalid,
                "A page cannot start before the first purchase.",
                ("skip", skip));
        }

        if (from is { } lower && to is { } upper && upper < lower)
        {
            throw ExpensesException.For(
                ApplicationErrors.ListingRangeInvalid,
                "The end of the range falls before its start.",
                ("from", lower),
                ("to", upper));
        }

        // Both bounds are inclusive dates, as a user reads them; the repository turns the upper
        // bound into an exclusive instant so that a purchase late on the last day is included.
        var listed = await purchases.List(
            new PurchaseListQuery(from, to, skip, resolvedTake),
            cancellationToken);

        return [.. listed.Select(purchase => PurchaseView.Of(purchase))];
    }

    /// <summary>
    /// Reads the temporary capture, promotes its bytes into the permanent store, and builds the
    /// receipt already in the terminal state extraction reached at capture time вЂ” never Pending or
    /// Extracting, since none exists any more (D12).
    /// </summary>
    private async Task<Receipt> Promote(CapturedReceiptCommand capture, CancellationToken cancellationToken)
    {
        byte[] bytes = await tempStore.Read(capture.TempKey, cancellationToken)
            ?? throw ExpensesException.For(
                ApplicationErrors.CaptureNotFound,
                $"No capture was found for key {capture.TempKey}. It may already have been confirmed, "
                + "or removed by the daily cleanup.",
                ("tempKey", capture.TempKey));

        var stored = await images.Save(bytes, cancellationToken);

        Receipt receipt;
        try
        {
            receipt = stored.AsReceipt(capture.State, capture.FailureReason);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw DomainErrorTranslation.Receipt(exception);
        }

        if (capture.SuppliedIkof is not null || capture.SuppliedJikr is not null)
        {
            receipt.SupplyFiscalIdentifiers(capture.SuppliedIkof, capture.SuppliedJikr);
        }

        if (capture.ExtractedIkof is not null || capture.ExtractedJikr is not null)
        {
            receipt.RecordExtractedFiscalIdentifiers(
                capture.ExtractedIkof,
                capture.ExtractedJikr,
                capture.FiscalExtractedSource);
        }

        return receipt;
    }

    /// <summary>
    /// The caller's own date, else the invoice creation timestamp a fiscal QR decoded at capture,
    /// else rejected: a purchase always has a date, and nothing else is trusted to supply one (D5).
    /// </summary>
    private static DateTime ResolveOccurrence(DateTime? occurredAt, CapturedReceiptCommand? capture)
    {
        if (occurredAt is { } supplied)
        {
            return DateTime.SpecifyKind(supplied, DateTimeKind.Unspecified);
        }

        if (capture?.FiscalCreatedAt is { Length: > 0 } fiscal
            && DateTimeOffset.TryParse(fiscal, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            // No conversion is applied: the wall-clock component the receipt printed is what is
            // kept, exactly as a caller-supplied DateTime is (D5).
            return DateTime.SpecifyKind(parsed.DateTime, DateTimeKind.Unspecified);
        }

        throw ExpensesException.For(
            ApplicationErrors.PurchaseOccurrenceRequired,
            "A purchase requires a date. Supply one, or confirm a capture whose fiscal QR decoded one.");
    }

    /// <summary>
    /// The existing purchase is returned exactly as it stands: its expenses are not appended to or
    /// replaced, and neither is its merchant. A repeated request is absorbed, not applied (D3).
    /// </summary>
    private static RecordPurchaseResult AlreadyRecorded(Purchase existing)
        => new(PurchaseView.Of(existing), AlreadyRecorded: true);

    /// <summary>
    /// Checked here as well as inside the aggregate, so that the error can carry both amounts вЂ”
    /// the aggregate signals with a framework exception and holds no error codes (D22).
    /// </summary>
    private static void Reconcile(decimal amount, IReadOnlyList<Expense> lines)
    {
        decimal total = lines.Sum(line => line.Amount);
        if (total == amount)
        {
            return;
        }

        throw ExpensesException.For(
            ApplicationErrors.PurchaseReconciliationMismatch,
            $"The purchase amount is {amount} while its expenses sum to {total}.",
            ("amount", amount),
            ("expensesTotal", total));
    }
}
