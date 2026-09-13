namespace Expenses.Desktop.Core.Screens;

/// <summary>One entry a line's category or unit can be set to, addressed by code; an empty code is none.</summary>
public sealed record Choice(string Code, string Name);
