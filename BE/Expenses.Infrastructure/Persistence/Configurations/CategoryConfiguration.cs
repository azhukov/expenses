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
