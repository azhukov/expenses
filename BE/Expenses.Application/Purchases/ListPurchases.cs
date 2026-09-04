using Expenses.Application.Abstractions;
using Expenses.Application.Errors;

namespace Expenses.Application.Purchases;

/// <summary>Date-range filtering, most recent first, paged.</summary>
public sealed class ListPurchases(IPurchaseRepository purchases)
{
    /// <summary>The page size a caller gets when it asks for none.</summary>
    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 200;

    public async Task<IReadOnlyList<PurchaseView>> Execute(
        ListPurchasesQuery query,
        CancellationToken cancellationToken = default)
    {
        int take = query.Take ?? DefaultPageSize;

        if (take is < 1 || take > MaxPageSize)
        {
            throw ExpensesException.For(
                ApplicationErrors.ListingPageSizeInvalid,
                $"A page holds between 1 and {MaxPageSize} purchases.",
                ("take", take));
        }

        if (query.Skip < 0)
        {
            throw ExpensesException.For(
                ApplicationErrors.ListingPageSizeInvalid,
                "A page cannot start before the first purchase.",
                ("skip", query.Skip));
        }

        if (query is { From: { } from, To: { } to } && to < from)
        {
            throw ExpensesException.For(
                ApplicationErrors.ListingRangeInvalid,
                "The end of the range falls before its start.",
                ("from", from),
                ("to", to));
        }

        // Both bounds are inclusive dates, as a user reads them; the repository turns the upper
        // bound into an exclusive instant so that a purchase late on the last day is included.
        var listed = await purchases.List(
            new PurchaseListQuery(query.From, query.To, query.Skip, take),
            cancellationToken);

        return [.. listed.Select(purchase => PurchaseView.Of(purchase))];
    }
}
