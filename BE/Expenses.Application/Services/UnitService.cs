using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;

namespace Expenses.Application.Services;

/// <summary>
/// Units are fixed reference data (D15): there is no create, rename or deactivate use case here,
/// because a user does not edit them in this change.
/// </summary>
public sealed class UnitService(IUnitRepository units)
{
    public async Task<IReadOnlyList<UnitView>> List(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var listed = await units.List(includeInactive, cancellationToken);

        return [.. listed
            .OrderBy(unit => unit.Code, StringComparer.Ordinal)
            .Select(UnitView.Of)];
    }
}
