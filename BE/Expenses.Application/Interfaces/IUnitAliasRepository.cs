using Expenses.Domain.Entities;

namespace Expenses.Application.Interfaces;

/// <summary>
/// The spellings a receipt may print for a unit. Aliases arrive and are looked up already
/// normalised (<c>UnitText.Normalise</c>); the store compares them exactly.
/// </summary>
public interface IUnitAliasRepository
{
    /// <summary>Every alias, of active and inactive units alike. The dictionary is small.</summary>
    Task<IReadOnlyList<UnitAlias>> List(CancellationToken cancellationToken = default);

    Task<UnitAlias?> FindByAlias(string alias, CancellationToken cancellationToken = default);

    /// <summary>Tracked for the caller's save, so a learned alias commits with its expense (D16).</summary>
    Task Add(UnitAlias alias, CancellationToken cancellationToken = default);

    Task Remove(UnitAlias alias, CancellationToken cancellationToken = default);
}
