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

            // "Has a receipt image" is one fact, not three independently nullable ones (D11). One
            // line on purpose: a multi-line literal takes the checkout's line endings, and the model
            // would then differ from its snapshot on every machine but the one that generated it.
            table.HasCheckConstraint(
                "ck_purchases_receipt_all_or_nothing",
                "(receipt_storage_key IS NULL) = (receipt_content_type IS NULL) "
                + "AND (receipt_storage_key IS NULL) = (receipt_size_in_bytes IS NULL)");

            // The source columns are what tell EF a fiscal invoice is present, so they are null
            // together or set together (D36).
            table.HasCheckConstraint(
                "ck_purchases_fiscal_all_or_nothing",
                "(fiscal_extracted_source IS NULL) = (fiscal_payload_source IS NULL)");

            // The state describes what was read, so it exists exactly when an image or a fiscal
            // invoice does (D35).
            table.HasCheckConstraint(
                "ck_purchases_extraction_state_with_receipt",
                "(receipt_state IS NOT NULL) = (receipt_storage_key IS NOT NULL OR fiscal_payload_source IS NOT NULL)");

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

        // On the purchase rather than the image, because a purchase read from its fiscal code alone
        // has a state and no image (D35). The column names predate that and are kept: renaming
        // them would buy nothing but a migration that rewrites the table (D36).
        builder.Property(purchase => purchase.Extraction)
            .HasColumnName("receipt_state")
            .HasConversion<int>();

        builder.Property(purchase => purchase.ExtractionFailureReason)
            .HasColumnName("receipt_failure_reason")
            .HasColumnType("text");

        ConfigureReceipt(builder);
        ConfigureFiscalInvoice(builder);

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

            // Not unique: the store is content-addressed, so byte-identical receipts share one file
            // and two purchases may carry the same key. The index is there to answer "is anything
            // else still referencing this file" at deletion time (D11).
            receipt.HasIndex(value => value.StorageKey)
                .HasDatabaseName("ix_purchases_receipt_storage_key");
        });

        builder.Navigation(purchase => purchase.Receipt).IsRequired(false);
    }

    /// <summary>
    /// The fiscal invoice, on the same row as the image and independent of it (D35). EF reads an
    /// optional owned value as absent when every one of its columns is null, so the two source
    /// columns — never null for an invoice that exists — are what make a present invoice visible,
    /// and are null for every purchase that has none (D36).
    /// </summary>
    private static void ConfigureFiscalInvoice(EntityTypeBuilder<Purchase> builder)
    {
        builder.OwnsOne(purchase => purchase.Fiscal, fiscal =>
        {
            // As read, with no format imposed (D10). Held per source so a disagreement can be
            // reported rather than one value silently preferred (D20).
            fiscal.Property(value => value.FiscalIkofSupplied)
                .HasColumnName("fiscal_ikof_supplied")
                .HasColumnType("text");

            fiscal.Property(value => value.FiscalIkofExtracted)
                .HasColumnName("fiscal_ikof_extracted")
                .HasColumnType("text");

            fiscal.Property(value => value.FiscalJikrSupplied)
                .HasColumnName("fiscal_jikr_supplied")
                .HasColumnType("text");

            fiscal.Property(value => value.FiscalJikrExtracted)
                .HasColumnName("fiscal_jikr_extracted")
                .HasColumnType("text");

            fiscal.Property(value => value.FiscalExtractedSource)
                .HasColumnName("fiscal_extracted_source")
                .HasConversion<int>();

            // The payload verbatim, unbounded in the column because no format is imposed on it and
            // the length bound belongs at the trust boundary that accepts it, not here (D30, D32).
            fiscal.Property(value => value.FiscalPayload)
                .HasColumnName("fiscal_payload")
                .HasColumnType("text");

            fiscal.Property(value => value.FiscalPayloadSource)
                .HasColumnName("fiscal_payload_source")
                .HasConversion<int>();

            // Both derived from the values above; storing either would be a second source of truth.
            fiscal.Ignore(value => value.Corroboration);
            fiscal.Ignore(value => value.IsEmpty);

            // "Is this invoice already recorded" is asked at every capture, of whichever column the
            // code arrived in (D39). Not unique: a user may confirm through the warning.
            fiscal.HasIndex(value => value.FiscalIkofSupplied)
                .HasDatabaseName("ix_purchases_fiscal_ikof_supplied");

            fiscal.HasIndex(value => value.FiscalIkofExtracted)
                .HasDatabaseName("ix_purchases_fiscal_ikof_extracted");
        });

        builder.Navigation(purchase => purchase.Fiscal).IsRequired(false);
    }
}
