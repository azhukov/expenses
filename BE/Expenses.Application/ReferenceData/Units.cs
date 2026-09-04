using Expenses.Application.Abstractions;

namespace Expenses.Application.ReferenceData;

/// <summary>
/// Units are fixed reference data (D15): there is no create, rename or deactivate use case here,
/// because a user does not edit them in this change.
/// </summary>
public sealed class ListUnits(IUnitRepository units)
{
    public async Task<IReadOnlyList<UnitView>> Execute(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var listed = await units.List(includeInactive, cancellationToken);

        return listed
            .OrderBy(unit => unit.Code, StringComparer.Ordinal)
            .Select(UnitView.Of)
            .ToList();
    }
}
