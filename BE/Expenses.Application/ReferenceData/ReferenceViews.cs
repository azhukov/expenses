using Expenses.Domain;

namespace Expenses.Application.ReferenceData;

/// <summary>
/// A category as it is read back. Both the parent identifier and its code are carried, because
/// callers address entries by code (D8) while stored references are by identifier.
/// </summary>
public sealed record CategoryView(
    long Id,
    string Code,
    string Name,
    long? ParentId,
    string? ParentCode,
    bool IsSystem,
    bool IsActive)
{
    public static CategoryView Of(Category category) => new(
        category.Id,
        category.Code,
        category.Name,
        category.Parent?.Id ?? category.ParentId,
        category.Parent?.Code,
        category.IsSystem,
        category.IsActive);
}

public sealed record UnitView(
    long Id,
    string Code,
    string Name,
    string Symbol,
    Unit.UnitKind Kind,
    bool IsActive)
{
    public static UnitView Of(Unit unit) => new(
        unit.Id,
        unit.Code,
        unit.Name,
        unit.Symbol,
        unit.Kind,
        unit.IsActive);
}
