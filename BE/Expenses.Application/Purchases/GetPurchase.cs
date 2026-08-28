using Expenses.Application.Abstractions;
using Expenses.Application.Errors;

namespace Expenses.Application.Purchases;

public sealed class GetPurchase(IPurchaseRepository purchases)
{
    public async Task<PurchaseView> Execute(long id, CancellationToken cancellationToken = default)
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
}
