namespace Expenses.Application.Abstractions;

/// <summary>
/// One transaction per use case; the aggregate is saved whole (D16).
/// </summary>
public interface IUnitOfWork
{
    Task SaveChanges(CancellationToken cancellationToken = default);
}
