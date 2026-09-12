using Expenses.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Infrastructure.Persistence.Configurations;

internal sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("units", table =>
            table.HasCheckConstraint("ck_units_name_length", "length(name) <= 128"));

        builder.HasKey(unit => unit.Id);

        // Seeded by HasData, which supplies the identifiers, so the column cannot be
        // GENERATED ALWAYS as the other keys are (D15).
        builder.Property(unit => unit.Id).HasColumnName("id").UseIdentityByDefaultColumn();

        builder.Property(unit => unit.Code)
            .HasColumnName("code")
            .HasColumnType("varchar(16)")
            .IsRequired();

        builder.Property(unit => unit.Name)
            .HasColumnName("name")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(unit => unit.Symbol)
            .HasColumnName("symbol")
            .HasColumnType("varchar(16)")
            .IsRequired();

        builder.Property(unit => unit.Kind).HasColumnName("kind").HasConversion<int>();

        builder.Property(unit => unit.IsActive).HasColumnName("is_active");

        builder.HasIndex(unit => unit.Code)
            .HasDatabaseName("ix_units_code")
            .IsUnique();

        // Fixed reference data: model-managed rows are correct here because nobody edits them.
        // Categories are seeded the other way, by upsert, precisely because users do (D15).
        builder.HasData(UnitSeed.Rows);
    }
}
