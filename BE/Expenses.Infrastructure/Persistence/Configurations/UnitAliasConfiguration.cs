using Expenses.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Infrastructure.Persistence.Configurations;

/// <summary>
/// Aliases are a table of their own rather than a column on <c>units</c>: one spelling mapping to
/// one unit is a rule between rows, which a unique index states directly, and <c>units</c> rows are
/// owned by <c>HasData</c> and are not written at runtime (D15). The starting set is inserted once by
/// the migration that creates the table, not declared here, so an alias a user removes stays removed.
/// </summary>
internal sealed class UnitAliasConfiguration : IEntityTypeConfiguration<UnitAlias>
{
    public void Configure(EntityTypeBuilder<UnitAlias> builder)
    {
        builder.ToTable("unit_aliases");

        builder.HasKey(alias => alias.Id);

        builder.Property(alias => alias.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(alias => alias.UnitId).HasColumnName("unit_id");

        // Held normalised by the application, so a plain unique index is the whole of the
        // uniqueness rule — no functional index, no citext.
        builder.Property(alias => alias.Alias)
            .HasColumnName("alias")
            .HasColumnType("text")
            .IsRequired();

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(alias => alias.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(alias => alias.Alias)
            .HasDatabaseName("ix_unit_aliases_alias")
            .IsUnique();

        builder.HasIndex(alias => alias.UnitId).HasDatabaseName("ix_unit_aliases_unit_id");
    }
}
