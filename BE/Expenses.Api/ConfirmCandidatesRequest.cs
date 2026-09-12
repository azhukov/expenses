namespace Expenses.Api;

public sealed record ConfirmCandidatesRequest(IReadOnlyList<ExpenseRequest>? Expenses = null);
