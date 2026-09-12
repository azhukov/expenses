using Expenses.Application.Dtos;

namespace Expenses.Mcp.Tools;

/// <summary>
/// One arithmetic check, in wording an assistant can act on. The outcome is a decision about the
/// numbers rather than a score, so it is reported as one (D20).
/// </summary>
public sealed record ArithmeticCheckSummary(string Check, string Outcome, string Explanation)
{
    public static ArithmeticCheckSummary Of(ArithmeticCheck check)
        => new(check.Name, check.Outcome.ToString(), check.Description);
}
