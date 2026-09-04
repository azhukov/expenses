using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Expenses.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build a context without a startup project (D15). Migrations live here,
/// this project is not a host, and there are two hosts — neither of which should be privileged
/// over the other by being the one migrations happen to need.
///
/// The connection string is design-time only: it is read from <c>EXPENSES_CONNECTION</c> and
/// otherwise falls back to the local development database. Nothing at runtime uses it.
/// </summary>
public sealed class ExpensesDbContextFactory : IDesignTimeDbContextFactory<ExpensesDbContext>
{
    private const string DevelopmentConnection =
        "Host=localhost;Port=5432;Database=expenses;Username=expenses;Password=expenses";

    public ExpensesDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("EXPENSES_CONNECTION") ?? DevelopmentConnection;

        return new ExpensesDbContext(new DbContextOptionsBuilder<ExpensesDbContext>()
            .UseNpgsql(connection)
            .Options);
    }
}
