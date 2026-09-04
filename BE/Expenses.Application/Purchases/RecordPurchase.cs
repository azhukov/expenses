using System.Globalization;
using Expenses.Application.Abstractions;
using Expenses.Application.Errors;
using Expenses.Application.Merchants;
using Expenses.Domain;

namespace Expenses.Application.Purchases;

/// <summary>
/// Records a purchase, absorbing a repeated submission of the same request (D3, D4). Confirming a
/// capture is folded into the same call: a purchase is created whole, with its receipt attached
/// from the moment it exists, never in a separate step afterward.
/// </summary>
public sealed class RecordPurchase(
    IPurchaseRepository purchases,
    ICategoryRepository categories,
    IUnitRepository units,
    ResolveMerchant merchants,
    ITemporaryReceiptStore tempStore,
    IReceiptImageStore images,
    IUnitOfWork unitOfWork)
{
    public async Task<RecordPurchaseResult> Execute(
        RecordPurchaseCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Expenses.Count == 0)
        {
            throw ExpensesException.For(
                ApplicationErrors.PurchaseNoExpenses,
                "A purchase requires at least one expense.");
        }

        var occurredAt = ResolveOccurrence(command);

        // The fast path: one round trip, a clean result, and no exception used as control flow.
        // The unique index the catch below relies on is what makes the guarantee true when two
        // requests race (D4).
        if (await purchases.FindByOccurrenceAndAmount(occurredAt, command.Amount, cancellationToken) is { } already)
        {
            return AlreadyRecorded(already);
        }

        var lines = await ExpenseAssembly.Build(command.Expenses, categories, units, cancellationToken);
        Reconcile(command.Amount, lines);

        // Resolved only once the submission is known to be new, so a repeated request cannot add a
        // merchant as a side effect of being ignored.
        var merchant = command.Merchant is { } named
            ? await merchants.Execute(named.Text, named.TaxId, cancellationToken)
            : null;

        // Promoted only once every rejection that must leave the capture untouched has already
        // happened: a reconciliation failure above never touches the temporary store (D11, D12).
        var receipt = command.Capture is { } capture
            ? await Promote(capture, cancellationToken)
            : null;

        Purchase purchase;
        try
        {
            purchase = Purchase.Record(
                occurredAt,
                command.Amount,
                lines,
                merchant?.Merchant.Id,
                command.Merchant?.Text,
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
            var winner = await purchases.FindByOccurrenceAndAmount(occurredAt, command.Amount, cancellationToken)
                ?? throw ExpensesException.For(
                    ApplicationErrors.PurchaseNotFound,
                    "A purchase with this occurrence and amount was recorded concurrently but cannot be read back.",
                    ("occurredAt", occurredAt),
                    ("amount", command.Amount));

            if (command.Capture is { } raced)
            {
                await tempStore.Delete(raced.TempKey, cancellationToken);
            }

            return AlreadyRecorded(winner);
        }

        if (command.Capture is { } confirmed)
        {
            await tempStore.Delete(confirmed.TempKey, cancellationToken);
        }

        return new RecordPurchaseResult(
            PurchaseView.Of(purchase),
            AlreadyRecorded: false,
            merchant is null ? null : MerchantView.Of(merchant.Merchant),
            merchant?.NewlyAdded ?? false);
    }

    /// <summary>
    /// Reads the temporary capture, promotes its bytes into the permanent store, and builds the
    /// receipt already in the terminal state extraction reached at capture time — never Pending or
    /// Extracting, since none exists any more (D12).
    /// </summary>
    private async Task<Receipt> Promote(CapturedReceiptCommand capture, CancellationToken cancellationToken)
    {
        var bytes = await tempStore.Read(capture.TempKey, cancellationToken)
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
    private static DateTime ResolveOccurrence(RecordPurchaseCommand command)
    {
        if (command.OccurredAt is { } supplied)
        {
            return DateTime.SpecifyKind(supplied, DateTimeKind.Unspecified);
        }

        if (command.Capture?.FiscalCreatedAt is { Length: > 0 } fiscal
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
    private static RecordPurchaseResult AlreadyRecorded(Purchase existing) =>
        new(PurchaseView.Of(existing), AlreadyRecorded: true);

    /// <summary>
    /// Checked here as well as inside the aggregate, so that the error can carry both amounts —
    /// the aggregate signals with a framework exception and holds no error codes (D22).
    /// </summary>
    private static void Reconcile(decimal amount, IReadOnlyList<Expense> lines)
    {
        var total = lines.Sum(line => line.Amount);
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
