using Expenses.Domain.Entities;

namespace Expenses.Application.Dtos;

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
