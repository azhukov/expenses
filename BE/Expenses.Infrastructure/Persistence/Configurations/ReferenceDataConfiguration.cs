using Expenses.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Infrastructure.Persistence.Configurations;

/// <summary>
/// The three dictionary tables (D8). Names are lower snake_case (D23, reversed) and every column is
/// named explicitly, because it never matches the PascalCase property; hand-written SQL needs no
/// quoting as a result, since PostgreSQL folds an unquoted identifier to lower case anyway.
/// </summary>
internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories", table =>
            table.HasCheckConstraint("ck_categories_name_length", "length(name) <= 256"));

        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        // ASCII, uppercase, stable: the seeding key, the MCP argument vocabulary, and the
        // reference that survives a rename (D8).
        builder.Property(category => category.Code)
            .HasColumnName("code")
            .HasColumnType("varchar(64)")
            .IsRequired();

        builder.Property(category => category.Name)
            .HasColumnName("name")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(category => category.ParentId).HasColumnName("parent_id");
        builder.Property(category => category.IsSystem).HasColumnName("is_system");
        builder.Property(category => category.IsActive).HasColumnName("is_active");

        builder.HasIndex(category => category.Code)
            .HasDatabaseName("ix_categories_code")
            .IsUnique();

        builder.Metadata
            .FindNavigation(nameof(Category.Children))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne(category => category.Parent)
            .WithMany(category => category.Children)
            .HasForeignKey(category => category.ParentId)
            .HasConstraintName("FK_categories_categories_parent_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(category => category.ParentId).HasDatabaseName("ix_categories_parent_id");
    }
}

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

internal sealed class MerchantConfiguration : IEntityTypeConfiguration<Merchant>
{
    public void Configure(EntityTypeBuilder<Merchant> builder)
    {
        builder.ToTable("merchants", table =>
            table.HasCheckConstraint("ck_merchants_name_length", "length(name) <= 256"));

        builder.HasKey(merchant => merchant.Id);
        builder.Property(merchant => merchant.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(merchant => merchant.Name)
            .HasColumnName("name")
            .HasColumnType("text")
            .IsRequired();

        // Unique *when present*: a market stall issues no tax number, and several unidentified
        // sellers must be able to coexist (D18).
        builder.Property(merchant => merchant.TaxId).HasColumnName("tax_id").HasColumnType("varchar(32)");

        builder.Property(merchant => merchant.ParentId).HasColumnName("parent_id");
        builder.Property(merchant => merchant.IsActive).HasColumnName("is_active");

        builder.HasIndex(merchant => merchant.TaxId)
            .HasDatabaseName("ix_merchants_tax_id")
            .IsUnique()
            .HasFilter("tax_id IS NOT NULL");

        builder.HasIndex(merchant => merchant.Name)
            .HasDatabaseName("ix_merchants_name_trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        builder.Metadata
            .FindNavigation(nameof(Merchant.Children))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne(merchant => merchant.Parent)
            .WithMany(merchant => merchant.Children)
            .HasForeignKey(merchant => merchant.ParentId)
            .HasConstraintName("FK_merchants_merchants_parent_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(merchant => merchant.ParentId).HasDatabaseName("ix_merchants_parent_id");
    }
}
