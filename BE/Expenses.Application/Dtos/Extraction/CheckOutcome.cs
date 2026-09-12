namespace Expenses.Application.Dtos;

/// <summary>The outcome of one arithmetic check. "Not applicable" is not a failure (D20).</summary>
public enum CheckOutcome
{
    NotApplicable = 0,
    Passed = 1,
    Failed = 2,
}
