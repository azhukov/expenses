using Expenses.Application.Errors;
using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;

namespace Expenses.Application.Services;

internal static class ReceiptLookup
{
    public static async Task<Purchase> Require(
        this IPurchaseRepository purchases,
        long purchaseId,
        CancellationToken cancellationToken)
        => await purchases.FindById(purchaseId, cancellationToken)
        ?? throw ExpensesException.For(
            ApplicationErrors.PurchaseNotFound,
            $"There is no purchase with identifier {purchaseId}.",
            ("id", purchaseId));

    /// <summary>
    /// A receipt is addressed by its purchase and has no identity of its own (D11), so "not found"
    /// is always a statement about the purchase.
    /// </summary>
    public static Receipt RequireReceipt(this Purchase purchase)
        => purchase.Receipt
        ?? throw ExpensesException.For(
            ApplicationErrors.ReceiptImageNotFound,
            $"Purchase {purchase.Id} has no receipt image.",
            ("purchaseId", purchase.Id));

    /// <summary>
    /// The extraction state of a purchase that carries a receipt of either kind — an image, a fiscal
    /// invoice or both (D35). A purchase with neither has nothing extraction ever read.
    /// </summary>
    public static Purchase.ExtractionState RequireExtraction(this Purchase purchase)
        => purchase.Extraction
        ?? throw ExpensesException.For(
            ApplicationErrors.ReceiptImageNotFound,
            $"Purchase {purchase.Id} has no receipt.",
            ("purchaseId", purchase.Id));
}
