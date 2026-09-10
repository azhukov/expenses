using Expenses.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Infrastructure.Persistence.Configurations;

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("expenses", table =>
        {
            table.HasCheckConstraint("ck_expenses_description_length", "length(description) <= 512");
            table.HasCheckConstraint(
                "ck_expenses_category_raw_length",
                "category_raw IS NULL OR length(category_raw) <= 256");
            table.HasCheckConstraint(
                "ck_expenses_unit_raw_length",
                "unit_raw IS NULL OR length(unit_raw) <= 128");
        });

        builder.HasKey(expense => expense.Id);
        builder.Property(expense => expense.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        // The FK back to the aggregate root, a shadow property because Expense holds no reference
        // of its own (D2).
        builder.Property<long?>("PurchaseId").HasColumnName("purchase_id");
        builder.HasIndex("PurchaseId").HasDatabaseName("ix_expenses_purchase_id");

        builder.Property(expense => expense.Description)
            .HasColumnName("description")
            .HasColumnType("text")
            .IsRequired();

        // Fractional mass and volume; three decimals covers fuel volumes (D10).
        builder.Property(expense => expense.Quantity).HasColumnName("quantity").HasColumnType("numeric(12,3)");

        builder.Property(expense => expense.Amount).HasColumnName("amount").HasColumnType("numeric(19,2)");

        builder.Property(expense => expense.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(19,2)");

        // Descriptive, and null means the receipt printed no discount rather than a discount of
        // zero — a distinction reporting depends on (D19).
        builder.Property(expense => expense.ListUnitPrice)
            .HasColumnName("list_unit_price")
            .HasColumnType("numeric(19,2)");

        builder.Property(expense => expense.DiscountAmount)
            .HasColumnName("discount_amount")
            .HasColumnType("numeric(19,2)");

        builder.Property(expense => expense.CategoryRaw).HasColumnName("category_raw").HasColumnType("text");

        builder.Property(expense => expense.UnitRaw).HasColumnName("unit_raw").HasColumnType("text");

        // Derived for display and never stored, so it cannot become a second source of truth (D19).
        builder.Ignore(expense => expense.DiscountPercentage);

        builder.HasIndex(expense => expense.Description)
            .HasDatabaseName("ix_expenses_description_trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        builder.Property(expense => expense.CategoryId).HasColumnName("category_id");
        builder.Property(expense => expense.UnitId).HasColumnName("unit_id");

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(expense => expense.CategoryId)
            .HasConstraintName("FK_expenses_categories_category_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(expense => expense.UnitId)
            .HasConstraintName("FK_expenses_units_unit_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(expense => expense.CategoryId).HasDatabaseName("ix_expenses_category_id");
        builder.HasIndex(expense => expense.UnitId).HasDatabaseName("ix_expenses_unit_id");
    }
}
