using Expenses.Domain.Entities;

namespace Expenses.Application.Abstractions;

/// <summary>
/// Units are fixed reference data seeded via <c>HasData</c> (D15), so there is no <c>Add</c>:
/// a user cannot create one in this change.
/// </summary>
public interface IUnitRepository
{
    Task<Unit?> FindById(long id, CancellationToken cancellationToken = default);

    Task<Unit?> FindByCode(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Unit>> List(bool includeInactive, CancellationToken cancellationToken = default);
}
