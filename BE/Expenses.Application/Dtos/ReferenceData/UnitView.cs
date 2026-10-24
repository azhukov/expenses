using Expenses.Domain.Entities;

namespace Expenses.Application.Dtos;

/// <summary>A unit and the spellings a receipt may print for it, normalised and in ordinal order.</summary>
public sealed record UnitView(
    long Id,
    string Code,
    string Name,
    string Symbol,
    Unit.UnitKind Kind,
    bool IsActive,
    IReadOnlyList<string> Aliases)
{
    public static UnitView Of(Unit unit, IEnumerable<UnitAlias> aliases) => new(
        unit.Id,
        unit.Code,
        unit.Name,
        unit.Symbol,
        unit.Kind,
        unit.IsActive,
        [.. aliases
            .Where(alias => alias.UnitId == unit.Id)
            .Select(alias => alias.Alias)
            .Order(StringComparer.Ordinal)]);
}
