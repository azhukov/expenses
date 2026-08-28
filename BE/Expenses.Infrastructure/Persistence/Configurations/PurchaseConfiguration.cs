using Expenses.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Infrastructure.Persistence.Configurations;

/// <summary>
/// The aggregate root and its lines in one configuration, because they are written and read as one
/// thing: an expense has no repository and never loads on its own (D2).
///
/// Table and column names are PascalCase (D23), which is what the entity and its properties are
/// already called, so a column is named only where the mapping actually differs from the property.
/// SQL written by hand — a check constraint, an index filter — must quote every identifier, because
/// PostgreSQL folds an unquoted one to lower case and would not find it.
/// </summary>
internal sealed class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> builder)
    {
        builder.ToTable("Purchases", table =>
        {
            table.HasCheckConstraint(
                "ck_purchases_merchant_raw_length",
                """
                "MerchantRaw" IS NULL OR length("MerchantRaw") <= 512
                """);

            // "Has a receipt" is one fact, not six independently nullable ones (D11).
            table.HasCheckConstraint(
                "ck_purchases_receipt_all_or_nothing",
                """
                ("ReceiptContentHash" IS NULL AND "ReceiptStorageKey" IS NULL AND "ReceiptContentType" IS NULL
                    AND "ReceiptSizeInBytes" IS NULL AND "ReceiptState" IS NULL)
                OR ("ReceiptContentHash" IS NOT NULL AND "ReceiptStorageKey" IS NOT NULL
                    AND "ReceiptContentType" IS NOT NULL AND "ReceiptSizeInBytes" IS NOT NULL
                    AND "ReceiptState" IS NOT NULL)
                """);

            table.HasCheckConstraint(
                "ck_purchases_receipt_content_hash_length",
                $"""
                 "ReceiptContentHash" IS NULL OR length("ReceiptContentHash") = {Receipt.ContentHashLength}
                 """);

            table.HasCheckConstraint(
                "ck_purchases_receipt_storage_key_length",
                $"""
                 "ReceiptStorageKey" IS NULL OR length("ReceiptStorageKey") <= {Receipt.StorageKeyMaxLength}
                 """);
        });

        builder.HasKey(purchase => purchase.Id);
        builder.Property(purchase => purchase.Id).UseIdentityAlwaysColumn();

        // Wall-clock, no offset, no conversion in either direction (D5). Npgsql maps
        // DateTimeKind.Unspecified onto `timestamp without time zone` and back unchanged.
        builder.Property(purchase => purchase.OccurredAt)
            .HasColumnType("timestamp without time zone");

        builder.Property(purchase => purchase.Amount).HasColumnType("numeric(19,2)");

        // Kept whether or not the merchant resolved, and never erased by a later match (D9, D18).
        builder.Property(purchase => purchase.MerchantRaw).HasColumnType("text");

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

        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(purchase => purchase.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Reached through the backing field, so the aggregate keeps its private list and EF does
        // not need the domain loosened to suit it.
        builder.Metadata
            .FindNavigation(nameof(Purchase.Expenses))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(purchase => purchase.Expenses)
            .WithOne()
            .HasForeignKey("PurchaseId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(purchase => purchase.Expenses).AutoInclude();
    }

    /// <summary>
    /// The receipt is a value inside the aggregate, mapped onto columns of the same row rather than
    /// a table of its own (D2, D11). The bytes are not here at all: <c>ReceiptStorageKey</c> locates
    /// the file, and the ledger holds the identity of that file and nothing more.
    ///
    /// These are the columns that are named explicitly, because they are the ones whose names differ
    /// from the property: the owned value's <c>ContentHash</c> is the purchase's
    /// <c>ReceiptContentHash</c>, and EF's own convention for an owned reference would produce
    /// <c>Receipt_ContentHash</c> (D23).
    /// </summary>
    private static void ConfigureReceipt(EntityTypeBuilder<Purchase> builder)
    {
        builder.OwnsOne(purchase => purchase.Receipt, receipt =>
        {
            receipt.Property(value => value.ContentHash)
                .HasColumnName("ReceiptContentHash")
                .HasColumnType("bytea");

            // Stored as written rather than recomputed from the hash, so the layout of the store
            // can change without rewriting a single existing reference (D11).
            receipt.Property(value => value.StorageKey)
                .HasColumnName("ReceiptStorageKey")
                .HasColumnType("text");

            receipt.Property(value => value.ContentType)
                .HasColumnName("ReceiptContentType")
                .HasColumnType("varchar(128)");

            receipt.Property(value => value.SizeInBytes).HasColumnName("ReceiptSizeInBytes");

            receipt.Property(value => value.State)
                .HasColumnName("ReceiptState")
                .HasConversion<int>();

            receipt.Property(value => value.FailureReason)
                .HasColumnName("ReceiptFailureReason")
                .HasColumnType("text");

            // As read, with no format imposed (D10). Held per source so a disagreement can be
            // reported rather than one value silently preferred (D20).
            receipt.Property(value => value.FiscalIkofSupplied)
                .HasColumnName("FiscalIkofSupplied")
                .HasColumnType("text");

            receipt.Property(value => value.FiscalIkofExtracted)
                .HasColumnName("FiscalIkofExtracted")
                .HasColumnType("text");

            receipt.Property(value => value.FiscalJikrSupplied)
                .HasColumnName("FiscalJikrSupplied")
                .HasColumnType("text");

            receipt.Property(value => value.FiscalJikrExtracted)
                .HasColumnName("FiscalJikrExtracted")
                .HasColumnType("text");

            receipt.Property(value => value.FiscalExtractedSource)
                .HasColumnName("FiscalExtractedSource")
                .HasConversion<int>();

            // Derived from the four values above; storing it would be a second source of truth.
            receipt.Ignore(value => value.Corroboration);

            // Not unique: byte-identical receipts share one file, so two purchases may carry the
            // same hash. The index is there to answer "is anything else still referencing this
            // file" at deletion time (D11).
            receipt.HasIndex(value => value.ContentHash)
                .HasDatabaseName("ix_purchases_receipt_content_hash");
        });

        builder.Navigation(purchase => purchase.Receipt).IsRequired(false);
    }
}

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("Expenses", table =>
        {
            table.HasCheckConstraint("ck_expenses_description_length", """length("Description") <= 512""");
            table.HasCheckConstraint(
                "ck_expenses_category_raw_length",
                """
                "CategoryRaw" IS NULL OR length("CategoryRaw") <= 256
                """);
            table.HasCheckConstraint(
                "ck_expenses_unit_raw_length",
                """
                "UnitRaw" IS NULL OR length("UnitRaw") <= 128
                """);
        });

        builder.HasKey(expense => expense.Id);
        builder.Property(expense => expense.Id).UseIdentityAlwaysColumn();

        builder.Property(expense => expense.Description)
            .HasColumnType("text")
            .IsRequired();

        // Fractional mass and volume; three decimals covers fuel volumes (D10).
        builder.Property(expense => expense.Quantity).HasColumnType("numeric(12,3)");

        builder.Property(expense => expense.Amount).HasColumnType("numeric(19,2)");

        builder.Property(expense => expense.UnitPrice).HasColumnType("numeric(19,2)");

        // Descriptive, and null means the receipt printed no discount rather than a discount of
        // zero — a distinction reporting depends on (D19).
        builder.Property(expense => expense.ListUnitPrice).HasColumnType("numeric(19,2)");

        builder.Property(expense => expense.DiscountAmount).HasColumnType("numeric(19,2)");

        builder.Property(expense => expense.CategoryRaw).HasColumnType("text");

        builder.Property(expense => expense.UnitRaw).HasColumnType("text");

        // Derived for display and never stored, so it cannot become a second source of truth (D19).
        builder.Ignore(expense => expense.DiscountPercentage);

        builder.HasIndex(expense => expense.Description)
            .HasDatabaseName("ix_expenses_description_trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(expense => expense.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(expense => expense.UnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
