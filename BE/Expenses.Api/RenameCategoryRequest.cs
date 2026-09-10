namespace Expenses.Api;

/// <summary>
/// <see cref="Code"/> is carried so that an attempt to change it can be refused rather than
/// silently ignored — the code is the identity stored references point at (D8).
/// </summary>
public sealed record RenameCategoryRequest(string Name, string? Code = null);
