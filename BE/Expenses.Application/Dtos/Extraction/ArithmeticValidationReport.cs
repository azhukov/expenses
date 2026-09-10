namespace Expenses.Application.Dtos;

/// <summary>The full report. Failing fewer checks is what makes two failed results comparable (D20).</summary>
public sealed record ArithmeticValidationReport(IReadOnlyList<ArithmeticCheck> Checks)
{
    /// <summary>True when no check failed. Checks that could not be performed do not count against it.</summary>
    public bool Passed => Checks.All(check => check.Outcome != CheckOutcome.Failed);

    public int FailedCount => Checks.Count(check => check.Outcome == CheckOutcome.Failed);

    public IEnumerable<ArithmeticCheck> Failures
        => Checks.Where(check => check.Outcome == CheckOutcome.Failed);
}
