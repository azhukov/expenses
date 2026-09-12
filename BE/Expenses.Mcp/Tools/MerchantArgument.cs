using System.ComponentModel;
using Expenses.Application.Dtos;

namespace Expenses.Mcp.Tools;

public sealed record MerchantArgument(
    [property: Description("The merchant as printed on the receipt or named by the user.")]
    string Text,
    [property: Description("The merchant's tax identification number, where the receipt printed one.")]
    string? TaxId = null)
{
    public MerchantCommand ToCommand() => new(Text, TaxId);
}
