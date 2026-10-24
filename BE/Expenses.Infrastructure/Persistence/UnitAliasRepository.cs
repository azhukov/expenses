using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Infrastructure.Persistence;

internal sealed class UnitAliasRepository(ExpensesDbContext context) : IUnitAliasRepository
{
    public async Task<IReadOnlyList<UnitAlias>> List(CancellationToken cancellationToken = default)
        => await context.UnitAliases
            .OrderBy(alias => alias.Alias)
            .ToListAsync(cancellationToken);

    public async Task<UnitAlias?> FindByAlias(string alias, CancellationToken cancellationToken = default)
        => await context.UnitAliases.FirstOrDefaultAsync(existing => existing.Alias == alias, cancellationToken);

    public async Task Add(UnitAlias alias, CancellationToken cancellationToken = default)
        => await context.UnitAliases.AddAsync(alias, cancellationToken);

    public Task Remove(UnitAlias alias, CancellationToken cancellationToken = default)
    {
        context.UnitAliases.Remove(alias);
        return Task.CompletedTask;
    }
}
