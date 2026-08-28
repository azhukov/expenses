using Expenses.Application.Abstractions;
using Expenses.Application.Errors;
using Expenses.Application.Merchants;
using Expenses.Domain;

namespace Expenses.Application.Purchases;

/// <summary>
/// Records a purchase, absorbing a repeated submission of the same request (D3, D4).
/// </summary>
public sealed class RecordPurchase(
    IPurchaseRepository purchases,
    ICategoryRepository categories,
    IUnitRepository units,
    ResolveMerchant merchants,
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

        // Never converted, and never given a kind it did not arrive with (D5).
        var occurredAt = DateTime.SpecifyKind(command.OccurredAt, DateTimeKind.Unspecified);

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

        Purchase purchase;
        try
        {
            purchase = Purchase.Record(
                occurredAt,
                command.Amount,
                lines,
                merchant?.Merchant.Id,
                command.Merchant?.Text);
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
            // winner is what both callers asked for, so re-query and hand it back as a success.
            var winner = await purchases.FindByOccurrenceAndAmount(occurredAt, command.Amount, cancellationToken)
                ?? throw ExpensesException.For(
                    ApplicationErrors.PurchaseNotFound,
                    "A purchase with this occurrence and amount was recorded concurrently but cannot be read back.",
                    ("occurredAt", occurredAt),
                    ("amount", command.Amount));

            return AlreadyRecorded(winner);
        }

        return new RecordPurchaseResult(
            PurchaseView.Of(purchase),
            AlreadyRecorded: false,
            merchant is null ? null : MerchantView.Of(merchant.Merchant),
            merchant?.NewlyAdded ?? false);
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
