namespace Expenses.Application.Dtos;

/// <summary>Names of the checks, stable because they are reported to both adapters.</summary>
public static class ArithmeticChecks
{
    public const string LineSum = "line_sum";
    public const string LineDiscount = "line_discount";
    public const string TotalTax = "total_tax";
}
