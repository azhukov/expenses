namespace Expenses.Desktop.Core.Rules;

/// <summary>
/// One line as the user is editing it. Every numeric field is the text the user typed: a half-typed
/// "3." is not a number, and parsing on every keystroke would fight the typing. Matches are held as
/// codes, because that is how the API addresses them, while the raw texts travel through untouched.
/// </summary>
public sealed record EditableLine(
    int Key,
    string Description,
    string Amount,
    string Quantity,
    string CategoryCode,
    string UnitCode,
    string? CategoryRaw,
    string? UnitRaw,
    decimal? UnitPrice,
    decimal? ListUnitPrice,
    decimal? DiscountAmount);
