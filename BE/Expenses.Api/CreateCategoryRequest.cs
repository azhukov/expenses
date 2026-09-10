namespace Expenses.Api;

public sealed record CreateCategoryRequest(string Code, string Name, string? ParentCode = null);
