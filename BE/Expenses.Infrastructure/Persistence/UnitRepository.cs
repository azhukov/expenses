using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Infrastructure.Persistence;

internal sealed class UnitRepository(ExpensesDbContext context) : IUnitRepository
{
    public async Task<Unit?> FindById(long id, CancellationToken cancellationToken = default)
        => await context.Units.FirstOrDefaultAsync(unit => unit.Id == id, cancellationToken);

    public async Task<Unit?> FindByCode(string code, CancellationToken cancellationToken = default)
        => await context.Units.FirstOrDefaultAsync(unit => EF.Functions.ILike(unit.Code, code), cancellationToken);

    public async Task<IReadOnlyList<Unit>> List(
        bool includeInactive,
        CancellationToken cancellationToken = default)
        => await context.Units
            .Where(unit => includeInactive || unit.IsActive)
            .OrderBy(unit => unit.Code)
            .ToListAsync(cancellationToken);
}
