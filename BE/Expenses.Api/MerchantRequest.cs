using Expenses.Application.Dtos;

namespace Expenses.Api;

public sealed record MerchantRequest(string Text, string? TaxId = null)
{
    public MerchantCommand ToCommand() => new(Text, TaxId);
}
