using Expenses.Application.Errors;
using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;

namespace Expenses.Application.Services;

internal static class MerchantLookup
{
    public static async Task<Merchant> Require(
        this IMerchantRepository merchants,
        long id,
        CancellationToken cancellationToken)
        => await merchants.FindById(id, cancellationToken)
        ?? throw ExpensesException.For(
            ApplicationErrors.MerchantNotFound,
            $"There is no merchant with identifier {id}.",
            ("id", id));
}
