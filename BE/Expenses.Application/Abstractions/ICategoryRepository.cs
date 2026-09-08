using Expenses.Domain.Entities;

namespace Expenses.Application.Abstractions;

public interface ICategoryRepository
{
    Task<Category?> FindById(long id, CancellationToken cancellationToken = default);

    /// <summary>Resolution is by <c>code</c>, which is the stable identity (D8).</summary>
    Task<Category?> FindByCode(string code, CancellationToken cancellationToken = default);

    /// <summary>Display order is stable, so two listings with no intervening change agree.</summary>
    Task<IReadOnlyList<Category>> List(bool includeInactive, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Category>> ActiveChildrenOf(long categoryId, CancellationToken cancellationToken = default);

    Task Add(Category category, CancellationToken cancellationToken = default);

    /// <summary>Only ever reached for a user-created entry; a seeded one is undeletable (D8).</summary>
    Task Remove(Category category, CancellationToken cancellationToken = default);
}
