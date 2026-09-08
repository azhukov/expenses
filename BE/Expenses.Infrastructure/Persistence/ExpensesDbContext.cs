using Expenses.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Infrastructure.Persistence;

/// <summary>
/// The one context. EF Core is the persistence abstraction and PostgreSQL features are used
/// deliberately and directly, so nothing here is written to be portable to another provider.
/// </summary>
public sealed class ExpensesDbContext(DbContextOptions<ExpensesDbContext> options) : DbContext(options)
{
    public DbSet<Purchase> Purchases => Set<Purchase>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Unit> Units => Set<Unit>();

    public DbSet<Merchant> Merchants => Set<Merchant>();

    /// <summary>
    /// Column types are decisions rather than preferences (D10): money is <c>numeric(19,2)</c>,
    /// quantity <c>numeric(12,3)</c>, and <c>occurred_at</c> is <c>timestamp</c> without time zone
    /// and never converted (D5). Entities keep private constructors and private setters, so the
    /// configuration reaches their backing fields rather than the domain being loosened to suit EF.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Search is trigram over unaccented text rather than full-text: receipt lines are short,
        // abbreviated and frequently mis-OCR'd, which is poor input for stemming and would need a
        // per-language configuration nothing here can reliably choose (D14).
        modelBuilder.HasPostgresExtension("unaccent");
        modelBuilder.HasPostgresExtension("pg_trgm");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ExpensesDbContext).Assembly);
    }
}
