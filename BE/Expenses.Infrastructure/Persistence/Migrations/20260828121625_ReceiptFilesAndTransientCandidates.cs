using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Expenses.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptFilesAndTransientCandidates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_purchases_receipt_images_receipt_image_id",
                table: "purchases");

            migrationBuilder.DropTable(
                name: "extraction_candidates");

            migrationBuilder.DropTable(
                name: "extraction_results");

            migrationBuilder.DropTable(
                name: "receipt_images");

            migrationBuilder.DropIndex(
                name: "IX_purchases_receipt_image_id",
                table: "purchases");

            // Dropped and added rather than renamed, which is what EF scaffolded: the old column
            // held the identifier of a row in a table that no longer exists, and carrying those
            // values into a size would be silently wrong data rather than a migration.
            migrationBuilder.DropColumn(
                name: "receipt_image_id",
                table: "purchases");

            migrationBuilder.AddColumn<long>(
                name: "receipt_size_in_bytes",
                table: "purchases",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "fiscal_extracted_source",
                table: "purchases",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fiscal_ikof_extracted",
                table: "purchases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fiscal_ikof_supplied",
                table: "purchases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fiscal_jikr_extracted",
                table: "purchases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fiscal_jikr_supplied",
                table: "purchases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "receipt_content_hash",
                table: "purchases",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receipt_content_type",
                table: "purchases",
                type: "varchar(128)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receipt_failure_reason",
                table: "purchases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "receipt_state",
                table: "purchases",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receipt_storage_key",
                table: "purchases",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchases_receipt_content_hash",
                table: "purchases",
                column: "receipt_content_hash");

            migrationBuilder.AddCheckConstraint(
                name: "ck_purchases_receipt_all_or_nothing",
                table: "purchases",
                sql: "(receipt_content_hash IS NULL AND receipt_storage_key IS NULL AND receipt_content_type IS NULL\n    AND receipt_size_in_bytes IS NULL AND receipt_state IS NULL)\nOR (receipt_content_hash IS NOT NULL AND receipt_storage_key IS NOT NULL\n    AND receipt_content_type IS NOT NULL AND receipt_size_in_bytes IS NOT NULL\n    AND receipt_state IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_purchases_receipt_content_hash_length",
                table: "purchases",
                sql: "receipt_content_hash IS NULL OR length(receipt_content_hash) = 32");

            migrationBuilder.AddCheckConstraint(
                name: "ck_purchases_receipt_storage_key_length",
                table: "purchases",
                sql: "receipt_storage_key IS NULL OR length(receipt_storage_key) <= 256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.DropCheckConstraint(
                name: "ck_purchases_receipt_storage_key_length",
                table: "purchases");

            migrationBuilder.DropColumn(
                name: "fiscal_extracted_source",
                table: "purchases");

            migrationBuilder.DropColumn(
                name: "fiscal_ikof_extracted",
                table: "purchases");

            migrationBuilder.DropColumn(
                name: "fiscal_ikof_supplied",
                table: "purchases");

            migrationBuilder.DropColumn(
                name: "fiscal_jikr_extracted",
                table: "purchases");

            migrationBuilder.DropColumn(
                name: "fiscal_jikr_supplied",
                table: "purchases");

            migrationBuilder.DropColumn(
                name: "receipt_content_hash",
                table: "purchases");

            migrationBuilder.DropColumn(
                name: "receipt_content_type",
                table: "purchases");

            migrationBuilder.DropColumn(
                name: "receipt_failure_reason",
                table: "purchases");

            migrationBuilder.DropColumn(
                name: "receipt_state",
                table: "purchases");

            migrationBuilder.DropColumn(
                name: "receipt_storage_key",
                table: "purchases");

            migrationBuilder.DropColumn(
                name: "receipt_size_in_bytes",
                table: "purchases");

            // The reverse is a schema rollback, not a data one: the images and candidates this
            // migration dropped are not recoverable from anything the database still holds, and the
            // receipt files stay in the store either way (D11).
            migrationBuilder.AddColumn<long>(
                name: "receipt_image_id",
                table: "purchases",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "receipt_images",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    content_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    content_type = table.Column<string>(type: "varchar(128)", nullable: false),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    fiscal_extracted_source = table.Column<int>(type: "integer", nullable: false),
                    fiscal_ikof_extracted = table.Column<string>(type: "text", nullable: true),
                    fiscal_ikof_supplied = table.Column<string>(type: "text", nullable: true),
                    fiscal_jikr_extracted = table.Column<string>(type: "text", nullable: true),
                    fiscal_jikr_supplied = table.Column<string>(type: "text", nullable: true),
                    size_in_bytes = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_images", x => x.id);
                    table.CheckConstraint("ck_receipt_images_content_hash_length", "length(content_hash) = 32");
                });

            migrationBuilder.CreateTable(
                name: "extraction_results",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    engine_name = table.Column<string>(type: "varchar(64)", nullable: false),
                    engine_version = table.Column<string>(type: "varchar(32)", nullable: false),
                    merchant_name = table.Column<string>(type: "text", nullable: true),
                    merchant_tax_id = table.Column<string>(type: "varchar(32)", nullable: true),
                    provenance = table.Column<string>(type: "jsonb", nullable: false),
                    receipt_image_id = table.Column<long>(type: "bigint", nullable: false),
                    reported_confidence = table.Column<string>(type: "jsonb", nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(19,2)", nullable: true),
                    tax_rate_percent = table.Column<decimal>(type: "numeric(9,4)", nullable: true),
                    total = table.Column<decimal>(type: "numeric(19,2)", nullable: true),
                    stages_run = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_extraction_results", x => x.id);
                    table.ForeignKey(
                        name: "FK_extraction_results_receipt_images_receipt_image_id",
                        column: x => x.receipt_image_id,
                        principalTable: "receipt_images",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "extraction_candidates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    amount = table.Column<decimal>(type: "numeric(19,2)", nullable: false),
                    category_id = table.Column<long>(type: "bigint", nullable: true),
                    category_raw = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(19,2)", nullable: true),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    list_unit_price = table.Column<decimal>(type: "numeric(19,2)", nullable: true),
                    provenance = table.Column<string>(type: "jsonb", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,3)", nullable: true),
                    reported_confidence = table.Column<string>(type: "jsonb", nullable: false),
                    unit_id = table.Column<long>(type: "bigint", nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(19,2)", nullable: true),
                    unit_raw = table.Column<string>(type: "text", nullable: true),
                    extraction_result_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_extraction_candidates", x => x.id);
                    table.ForeignKey(
                        name: "FK_extraction_candidates_extraction_results_extraction_result_~",
                        column: x => x.extraction_result_id,
                        principalTable: "extraction_results",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchases_receipt_image_id",
                table: "purchases",
                column: "receipt_image_id");

            migrationBuilder.CreateIndex(
                name: "IX_extraction_candidates_extraction_result_id",
                table: "extraction_candidates",
                column: "extraction_result_id");

            migrationBuilder.CreateIndex(
                name: "IX_extraction_results_receipt_image_id",
                table: "extraction_results",
                column: "receipt_image_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_images_content_hash",
                table: "receipt_images",
                column: "content_hash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_purchases_receipt_images_receipt_image_id",
                table: "purchases",
                column: "receipt_image_id",
                principalTable: "receipt_images",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
