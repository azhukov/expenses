using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expenses.Infrastructure.Persistence.Migrations;

/// <summary>
/// The content hash is dropped: the receipt store is content-addressed, so
/// <c>receipt_storage_key</c> already carries the hash and identifies the file more precisely than
/// the hash does — two receipts with the same bytes but a different detected content type share a
/// hash while being two files. Its one reader was the "is anything else still referencing this
/// file" check at deletion time (D11), which now counts storage keys, so the index moves with it.
/// </summary>
public partial class DropReceiptContentHash : Migration
{
    /// <summary>
    /// The hex of the hash inside a storage key of the form <c>ab/cd/&lt;64 hex&gt;.jpg</c> — the
    /// only layout that has ever been written, which is what makes the down migration exact.
    /// </summary>
    private const string HashHexFromStorageKey = "substring(receipt_storage_key from 7 for 64)";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_purchases_receipt_content_hash",
            table: "purchases");

        migrationBuilder.DropCheckConstraint(
            name: "ck_purchases_receipt_all_or_nothing",
            table: "purchases");

        migrationBuilder.DropCheckConstraint(
            name: "ck_purchases_receipt_content_hash_length",
            table: "purchases");

        migrationBuilder.DropColumn(
            name: "receipt_content_hash",
            table: "purchases");

        migrationBuilder.CreateIndex(
            name: "ix_purchases_receipt_storage_key",
            table: "purchases",
            column: "receipt_storage_key");

        migrationBuilder.AddCheckConstraint(
            name: "ck_purchases_receipt_all_or_nothing",
            table: "purchases",
            sql: """
                (receipt_storage_key IS NULL AND receipt_content_type IS NULL
                    AND receipt_size_in_bytes IS NULL AND receipt_state IS NULL)
                OR (receipt_storage_key IS NOT NULL AND receipt_content_type IS NOT NULL
                    AND receipt_size_in_bytes IS NOT NULL AND receipt_state IS NOT NULL)
                """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_purchases_receipt_storage_key",
            table: "purchases");

        migrationBuilder.DropCheckConstraint(
            name: "ck_purchases_receipt_all_or_nothing",
            table: "purchases");

        migrationBuilder.AddColumn<byte[]>(
            name: "receipt_content_hash",
            table: "purchases",
            type: "bytea",
            nullable: true);

        // Recovered from the storage key rather than left null: the column is back under the
        // all-or-nothing constraint below, so a receipt without a hash would fail it.
        migrationBuilder.Sql(
            $"""
            UPDATE purchases
            SET receipt_content_hash = decode({HashHexFromStorageKey}, 'hex')
            WHERE receipt_storage_key IS NOT NULL
            """);

        migrationBuilder.CreateIndex(
            name: "ix_purchases_receipt_content_hash",
            table: "purchases",
            column: "receipt_content_hash");

        migrationBuilder.AddCheckConstraint(
            name: "ck_purchases_receipt_all_or_nothing",
            table: "purchases",
            sql: """
                (receipt_content_hash IS NULL AND receipt_storage_key IS NULL AND receipt_content_type IS NULL
                    AND receipt_size_in_bytes IS NULL AND receipt_state IS NULL)
                OR (receipt_content_hash IS NOT NULL AND receipt_storage_key IS NOT NULL
                    AND receipt_content_type IS NOT NULL AND receipt_size_in_bytes IS NOT NULL
                    AND receipt_state IS NOT NULL)
                """);

        migrationBuilder.AddCheckConstraint(
            name: "ck_purchases_receipt_content_hash_length",
            table: "purchases",
            sql: "receipt_content_hash IS NULL OR length(receipt_content_hash) = 32");
    }
}
