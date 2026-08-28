using Expenses.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Infrastructure.Persistence.Configurations;

/// <summary>
/// The three dictionary tables (D8). Names are PascalCase and match the entity and its properties,
/// so nothing is named here twice; hand-written SQL quotes its identifiers, because PostgreSQL folds
/// an unquoted one to lower case (D23).
/// </summary>
internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories", table =>
            table.HasCheckConstraint("ck_categories_name_length", """length("Name") <= 256"""));

        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).UseIdentityAlwaysColumn();

        // ASCII, uppercase, stable: the seeding key, the MCP argument vocabulary, and the
        // reference that survives a rename (D8).
        builder.Property(category => category.Code)
            .HasColumnType("varchar(64)")
            .IsRequired();

        builder.Property(category => category.Name)
            .HasColumnType("text")
            .IsRequired();

        builder.HasIndex(category => category.Code)
            .HasDatabaseName("ix_categories_code")
            .IsUnique();

        builder.Metadata
            .FindNavigation(nameof(Category.Children))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne(category => category.Parent)
            .WithMany(category => category.Children)
            .HasForeignKey(category => category.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("Units", table =>
            table.HasCheckConstraint("ck_units_name_length", """length("Name") <= 128"""));

        builder.HasKey(unit => unit.Id);

        // Seeded by HasData, which supplies the identifiers, so the column cannot be
        // GENERATED ALWAYS as the other keys are (D15).
        builder.Property(unit => unit.Id).UseIdentityByDefaultColumn();

        builder.Property(unit => unit.Code)
            .HasColumnType("varchar(16)")
            .IsRequired();

        builder.Property(unit => unit.Name)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(unit => unit.Symbol)
            .HasColumnType("varchar(16)")
            .IsRequired();

        builder.Property(unit => unit.Kind).HasConversion<int>();

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
        builder.ToTable("Merchants", table =>
            table.HasCheckConstraint("ck_merchants_name_length", """length("Name") <= 256"""));

        builder.HasKey(merchant => merchant.Id);
        builder.Property(merchant => merchant.Id).UseIdentityAlwaysColumn();

        builder.Property(merchant => merchant.Name)
            .HasColumnType("text")
            .IsRequired();

        // Unique *when present*: a market stall issues no tax number, and several unidentified
        // sellers must be able to coexist (D18).
        builder.Property(merchant => merchant.TaxId).HasColumnType("varchar(32)");

        builder.HasIndex(merchant => merchant.TaxId)
            .HasDatabaseName("ix_merchants_tax_id")
            .IsUnique()
            .HasFilter("""
                       "TaxId" IS NOT NULL
                       """);

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
            .OnDelete(DeleteBehavior.Restrict);
    }
}
