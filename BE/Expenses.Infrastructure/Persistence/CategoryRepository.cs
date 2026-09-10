using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Infrastructure.Persistence;

internal sealed class CategoryRepository(ExpensesDbContext context) : ICategoryRepository
{
    public async Task<Category?> FindById(long id, CancellationToken cancellationToken = default)
        => await context.Categories.FirstOrDefaultAsync(category => category.Id == id, cancellationToken);

    /// <summary>
    /// Codes are ASCII and uppercase by convention, but a caller that types one in lower case is
    /// naming the same entry, so the comparison is case-insensitive rather than the caller having
    /// to know the convention (D8).
    /// </summary>
    public async Task<Category?> FindByCode(string code, CancellationToken cancellationToken = default)
        => await context.Categories.FirstOrDefaultAsync(
            category => EF.Functions.ILike(category.Code, code),
            cancellationToken);

    public async Task<IReadOnlyList<Category>> List(
        bool includeInactive,
        CancellationToken cancellationToken = default)
        => await context.Categories
            .Where(category => includeInactive || category.IsActive)
            .OrderBy(category => category.Code)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Category>> ActiveChildrenOf(
        long categoryId,
        CancellationToken cancellationToken = default)
        => await context.Categories
            .Where(category => category.ParentId == categoryId && category.IsActive)
            .OrderBy(category => category.Code)
            .ToListAsync(cancellationToken);

    public async Task Add(Category category, CancellationToken cancellationToken = default)
        => await context.Categories.AddAsync(category, cancellationToken);

    public Task Remove(Category category, CancellationToken cancellationToken = default)
    {
        context.Categories.Remove(category);
        return Task.CompletedTask;
    }
}
