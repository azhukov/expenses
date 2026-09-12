using Expenses.Domain.Entities;

namespace Expenses.Application.Dtos;

public sealed record MerchantResolution(Merchant Merchant, MerchantMatchKind Kind)
{
    public bool NewlyAdded => Kind == MerchantMatchKind.Created;
}
