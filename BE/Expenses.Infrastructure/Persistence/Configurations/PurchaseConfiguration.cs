using Expenses.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Infrastructure.Persistence.Configurations;

/// <summary>
/// The aggregate root and its lines in one configuration, because they are written and read as one
/// thing: an expense has no repository and never loads on its own (D2).
///
/// Table and column names are lower snake_case (D23, reversed), the PostgreSQL house style, so
/// nothing written by hand — a check constraint, an index filter, a `psql` session — needs
/// quoting. Every property is named explicitly because the mapping from PascalCase C# to
/// snake_case SQL always differs from the property, not just occasionally.
/// </summary>
internal sealed class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> builder)
    {
        builder.ToTable("purchases", table =>
        {
            table.HasCheckConstraint(
                "ck_purchases_merchant_raw_length",
                "merchant_raw IS NULL OR length(merchant_raw) <= 512");

            // "Has a receipt" is one fact, not four independently nullable ones (D11).
            table.HasCheckConstraint(
                "ck_purchases_receipt_all_or_nothing",
                """
                (receipt_storage_key IS NULL AND receipt_content_type IS NULL
                    AND receipt_size_in_bytes IS NULL AND receipt_state IS NULL)
                OR (receipt_storage_key IS NOT NULL AND receipt_content_type IS NOT NULL
                    AND receipt_size_in_bytes IS NOT NULL AND receipt_state IS NOT NULL)
                """);

            table.HasCheckConstraint(
                "ck_purchases_receipt_storage_key_length",
                $"receipt_storage_key IS NULL OR length(receipt_storage_key) <= {Receipt.StorageKeyMaxLength}");
        });

        builder.HasKey(purchase => purchase.Id);
        builder.Property(purchase => purchase.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        // Wall-clock, no offset, no conversion in either direction (D5). Npgsql maps
        // DateTimeKind.Unspecified onto `timestamp without time zone` and back unchanged.
        builder.Property(purchase => purchase.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(purchase => purchase.Amount).HasColumnName("amount").HasColumnType("numeric(19,2)");

        // Kept whether or not the merchant resolved, and never erased by a later match (D9, D18).
        builder.Property(purchase => purchase.MerchantRaw).HasColumnName("merchant_raw").HasColumnType("text");

        ConfigureReceipt(builder);

        // The guarantee itself, rather than a check the application makes: two identical requests
        // arriving together is exactly the case a check-then-insert loses (D4).
        builder.HasIndex(purchase => new { purchase.OccurredAt, purchase.Amount })
            .HasDatabaseName("ix_purchases_occurred_at_amount")
            .IsUnique();

        // An unmatched merchant is the one a user searches for by half-remembered name (D14).
        builder.HasIndex(purchase => purchase.MerchantRaw)
            .HasDatabaseName("ix_purchases_merchant_raw_trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        builder.Property(purchase => purchase.MerchantId).HasColumnName("merchant_id");

        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(purchase => purchase.MerchantId)
            .HasConstraintName("FK_purchases_merchants_merchant_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(purchase => purchase.MerchantId).HasDatabaseName("ix_purchases_merchant_id");

        // Reached through the backing field, so the aggregate keeps its private list and EF does
        // not need the domain loosened to suit it.
        builder.Metadata
            .FindNavigation(nameof(Purchase.Expenses))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(purchase => purchase.Expenses)
            .WithOne()
            .HasForeignKey("PurchaseId")
            .HasConstraintName("FK_expenses_purchases_purchase_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(purchase => purchase.Expenses).AutoInclude();
    }

    /// <summary>
    /// The receipt is a value inside the aggregate, mapped onto columns of the same row rather than
    /// a table of its own (D2, D11). The bytes are not here at all: <c>ReceiptStorageKey</c> locates
    /// the file, and the ledger holds the identity of that file and nothing more.
    ///
    /// Every one of these is named explicitly in snake_case: not only does EF's own convention for
    /// an owned reference produce <c>Receipt_StorageKey</c> rather than <c>ReceiptStorageKey</c>,
    /// snake_case never matches a PascalCase property name to begin with (D23).
    /// </summary>
    private static void ConfigureReceipt(EntityTypeBuilder<Purchase> builder)
    {
        builder.OwnsOne(purchase => purchase.Receipt, receipt =>
        {
            // Stored as written rather than recomputed from the content, so the layout of the store
            // can change without rewriting a single existing reference (D11).
            receipt.Property(value => value.StorageKey)
                .HasColumnName("receipt_storage_key")
                .HasColumnType("text");

            receipt.Property(value => value.ContentType)
                .HasColumnName("receipt_content_type")
                .HasColumnType("varchar(128)");

            receipt.Property(value => value.SizeInBytes).HasColumnName("receipt_size_in_bytes");

            receipt.Property(value => value.State)
                .HasColumnName("receipt_state")
                .HasConversion<int>();

            receipt.Property(value => value.FailureReason)
                .HasColumnName("receipt_failure_reason")
                .HasColumnType("text");

            // As read, with no format imposed (D10). Held per source so a disagreement can be
            // reported rather than one value silently preferred (D20).
            receipt.Property(value => value.FiscalIkofSupplied)
                .HasColumnName("fiscal_ikof_supplied")
                .HasColumnType("text");

            receipt.Property(value => value.FiscalIkofExtracted)
                .HasColumnName("fiscal_ikof_extracted")
                .HasColumnType("text");

            receipt.Property(value => value.FiscalJikrSupplied)
                .HasColumnName("fiscal_jikr_supplied")
                .HasColumnType("text");

            receipt.Property(value => value.FiscalJikrExtracted)
                .HasColumnName("fiscal_jikr_extracted")
                .HasColumnType("text");

            receipt.Property(value => value.FiscalExtractedSource)
                .HasColumnName("fiscal_extracted_source")
                .HasConversion<int>();

            // Derived from the four values above; storing it would be a second source of truth.
            receipt.Ignore(value => value.Corroboration);

            // Not unique: the store is content-addressed, so byte-identical receipts share one file
            // and two purchases may carry the same key. The index is there to answer "is anything
            // else still referencing this file" at deletion time (D11).
            receipt.HasIndex(value => value.StorageKey)
                .HasDatabaseName("ix_purchases_receipt_storage_key");
        });

        builder.Navigation(purchase => purchase.Receipt).IsRequired(false);
    }
}
