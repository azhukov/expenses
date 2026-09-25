using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expenses.Infrastructure.Persistence.Migrations;

/// <summary>
/// The fiscal invoice moves off the receipt image and onto the purchase, and the extraction state with
/// it, so that a purchase read from its fiscal code alone has both and no image (D35).
///
/// No column is renamed, added or dropped: the same columns are mapped to a different owner (D36).
/// What changes is what a null means. EF reads an optional owned value as absent only when all its
/// columns are null, and the old mapping wrote both fiscal source columns as zero for every image,
/// fiscal code or not — so an image that carried no code would come back with an empty invoice. Those
/// two columns are cleared where no fiscal value was ever recorded, before the constraints that now
/// depend on them are added.
///
/// Rollback: <see cref="Down"/> restores the zeros and the old constraint, which a purchase with a
/// state and no image cannot satisfy. Purchases captured from a fiscal code alone must be dealt with
/// before it can run.
/// </summary>
public partial class SplitFiscalInvoiceFromReceipt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_purchases_receipt_all_or_nothing",
            table: "purchases");

        migrationBuilder.Sql(
            """
            UPDATE purchases
            SET fiscal_extracted_source = NULL, fiscal_payload_source = NULL
            WHERE fiscal_ikof_supplied IS NULL AND fiscal_ikof_extracted IS NULL
              AND fiscal_jikr_supplied IS NULL AND fiscal_jikr_extracted IS NULL
              AND fiscal_payload IS NULL;
            """);

        migrationBuilder.CreateIndex(
            name: "ix_purchases_fiscal_ikof_extracted",
            table: "purchases",
            column: "fiscal_ikof_extracted");

        migrationBuilder.CreateIndex(
            name: "ix_purchases_fiscal_ikof_supplied",
            table: "purchases",
            column: "fiscal_ikof_supplied");

        migrationBuilder.AddCheckConstraint(
            name: "ck_purchases_extraction_state_with_receipt",
            table: "purchases",
            sql: "(receipt_state IS NOT NULL) = (receipt_storage_key IS NOT NULL OR fiscal_payload_source IS NOT NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_purchases_fiscal_all_or_nothing",
            table: "purchases",
            sql: "(fiscal_extracted_source IS NULL) = (fiscal_payload_source IS NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_purchases_receipt_all_or_nothing",
            table: "purchases",
            sql: "(receipt_storage_key IS NULL) = (receipt_content_type IS NULL) AND (receipt_storage_key IS NULL) = (receipt_size_in_bytes IS NULL)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_purchases_fiscal_ikof_extracted",
            table: "purchases");

        migrationBuilder.DropIndex(
            name: "ix_purchases_fiscal_ikof_supplied",
            table: "purchases");

        migrationBuilder.DropCheckConstraint(
            name: "ck_purchases_extraction_state_with_receipt",
            table: "purchases");

        migrationBuilder.DropCheckConstraint(
            name: "ck_purchases_fiscal_all_or_nothing",
            table: "purchases");

        migrationBuilder.DropCheckConstraint(
            name: "ck_purchases_receipt_all_or_nothing",
            table: "purchases");

        // The old mapping read the source columns as non-nullable on every image.
        migrationBuilder.Sql(
            """
            UPDATE purchases
            SET fiscal_extracted_source = 0, fiscal_payload_source = 0
            WHERE receipt_storage_key IS NOT NULL AND fiscal_extracted_source IS NULL;
            """);

        migrationBuilder.AddCheckConstraint(
            name: "ck_purchases_receipt_all_or_nothing",
            table: "purchases",
            sql: "(receipt_storage_key IS NULL AND receipt_content_type IS NULL\n    AND receipt_size_in_bytes IS NULL AND receipt_state IS NULL)\nOR (receipt_storage_key IS NOT NULL AND receipt_content_type IS NOT NULL\n    AND receipt_size_in_bytes IS NOT NULL AND receipt_state IS NOT NULL)");
    }
}
