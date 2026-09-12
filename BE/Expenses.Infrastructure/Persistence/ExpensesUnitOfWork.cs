using Expenses.Application.Errors;
using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Expenses.Infrastructure.Persistence;

/// <summary>
/// One transaction per use case (D16), and the place a unique-index violation becomes the duplicate
/// outcome the guard expects rather than an infrastructure exception surfacing through it (D4).
/// </summary>
internal sealed class ExpensesUnitOfWork(ExpensesDbContext context) : IUnitOfWork
{
    private const string DuplicatePurchaseIndex = "ix_purchases_occurred_at_amount";

    public async Task SaveChanges(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: DuplicatePurchaseIndex,
            })
        {
            var losing = exception.Entries
                .Select(entry => entry.Entity)
                .OfType<Purchase>()
                .FirstOrDefault();

            // Detached so that a caller re-querying for the winner is not handed the row the
            // database has just refused from the change tracker.
            if (losing is not null)
            {
                context.Entry(losing).State = EntityState.Detached;
            }

            throw new DuplicatePurchaseException(
                losing?.OccurredAt ?? default,
                losing?.Amount ?? default,
                exception);
        }
    }
}
