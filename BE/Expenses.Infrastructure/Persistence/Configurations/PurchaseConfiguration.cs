using Expenses.Domain;
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

            // "Has a receipt" is one fact, not six independently nullable ones (D11).
            table.HasCheckConstraint(
                "ck_purchases_receipt_all_or_nothing",
                """
                (receipt_content_hash IS NULL AND receipt_storage_key IS NULL AND receipt_content_type IS NULL
                    AND receipt_size_in_bytes IS NULL AND receipt_state IS NULL)
                OR (receipt_content_hash IS NOT NULL AND receipt_storage_key IS NOT NULL
                    AND receipt_content_type IS NOT NULL AND receipt_size_in_bytes IS NOT NULL
                    AND receipt_state IS NOT NULL)
                """);

            table.HasCheckConstraint(
                "ck_purchases_receipt_content_hash_length",
                $"receipt_content_hash IS NULL OR length(receipt_content_hash) = {Receipt.ContentHashLength}");

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
    /// an owned reference produce <c>Receipt_ContentHash</c> rather than <c>ReceiptContentHash</c>,
    /// snake_case never matches a PascalCase property name to begin with (D23).
    /// </summary>
    private static void ConfigureReceipt(EntityTypeBuilder<Purchase> builder)
    {
        builder.OwnsOne(purchase => purchase.Receipt, receipt =>
        {
            receipt.Property(value => value.ContentHash)
                .HasColumnName("receipt_content_hash")
                .HasColumnType("bytea");

            // Stored as written rather than recomputed from the hash, so the layout of the store
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
