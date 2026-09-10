using Expenses.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Infrastructure.Persistence.Configurations;

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
