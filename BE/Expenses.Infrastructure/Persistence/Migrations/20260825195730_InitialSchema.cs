using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Expenses.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,");

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    code = table.Column<string>(type: "varchar(64)", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    parent_id = table.Column<long>(type: "bigint", nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.id);
                    table.CheckConstraint("ck_categories_name_length", "length(name) <= 256");
                    table.ForeignKey(
                        name: "FK_categories_categories_parent_id",
                        column: x => x.parent_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "merchants",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    tax_id = table.Column<string>(type: "varchar(32)", nullable: true),
                    parent_id = table.Column<long>(type: "bigint", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchants", x => x.id);
                    table.CheckConstraint("ck_merchants_name_length", "length(name) <= 256");
                    table.ForeignKey(
                        name: "FK_merchants_merchants_parent_id",
                        column: x => x.parent_id,
                        principalTable: "merchants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "receipt_images",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    content_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    content_type = table.Column<string>(type: "varchar(128)", nullable: false),
                    size_in_bytes = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    fiscal_ikof_supplied = table.Column<string>(type: "text", nullable: true),
                    fiscal_ikof_extracted = table.Column<string>(type: "text", nullable: true),
                    fiscal_jikr_supplied = table.Column<string>(type: "text", nullable: true),
                    fiscal_jikr_extracted = table.Column<string>(type: "text", nullable: true),
                    fiscal_extracted_source = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_images", x => x.id);
                    table.CheckConstraint("ck_receipt_images_content_hash_length", "length(content_hash) = 32");
                });

            migrationBuilder.CreateTable(
                name: "units",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "varchar(16)", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    symbol = table.Column<string>(type: "varchar(16)", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_units", x => x.id);
                    table.CheckConstraint("ck_units_name_length", "length(name) <= 128");
                });

            migrationBuilder.CreateTable(
                name: "extraction_results",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    receipt_image_id = table.Column<long>(type: "bigint", nullable: false),
                    engine_name = table.Column<string>(type: "varchar(64)", nullable: false),
                    engine_version = table.Column<string>(type: "varchar(32)", nullable: false),
                    total = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    tax_rate_percent = table.Column<decimal>(type: "numeric(9,4)", nullable: true),
                    tax_amount = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    merchant_name = table.Column<string>(type: "text", nullable: true),
                    merchant_tax_id = table.Column<string>(type: "varchar(32)", nullable: true),
                    provenance = table.Column<string>(type: "jsonb", nullable: false),
                    reported_confidence = table.Column<string>(type: "jsonb", nullable: false),
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
                name: "purchases",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    occurred_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    merchant_id = table.Column<long>(type: "bigint", nullable: true),
                    merchant_raw = table.Column<string>(type: "text", nullable: true),
                    receipt_image_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchases", x => x.id);
                    table.CheckConstraint("ck_purchases_merchant_raw_length", "merchant_raw IS NULL OR length(merchant_raw) <= 512");
                    table.ForeignKey(
                        name: "FK_purchases_merchants_merchant_id",
                        column: x => x.merchant_id,
                        principalTable: "merchants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchases_receipt_images_receipt_image_id",
                        column: x => x.receipt_image_id,
                        principalTable: "receipt_images",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "extraction_candidates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,3)", nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    list_unit_price = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    discount_amount = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    category_raw = table.Column<string>(type: "text", nullable: true),
                    unit_raw = table.Column<string>(type: "text", nullable: true),
                    category_id = table.Column<long>(type: "bigint", nullable: true),
                    unit_id = table.Column<long>(type: "bigint", nullable: true),
                    provenance = table.Column<string>(type: "jsonb", nullable: false),
                    reported_confidence = table.Column<string>(type: "jsonb", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "expenses",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    description = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    unit_id = table.Column<long>(type: "bigint", nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    category_id = table.Column<long>(type: "bigint", nullable: true),
                    category_raw = table.Column<string>(type: "text", nullable: true),
                    unit_raw = table.Column<string>(type: "text", nullable: true),
                    list_unit_price = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    discount_amount = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    purchase_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expenses", x => x.id);
                    table.CheckConstraint("ck_expenses_category_raw_length", "category_raw IS NULL OR length(category_raw) <= 256");
                    table.CheckConstraint("ck_expenses_description_length", "length(description) <= 512");
                    table.CheckConstraint("ck_expenses_unit_raw_length", "unit_raw IS NULL OR length(unit_raw) <= 128");
                    table.ForeignKey(
                        name: "FK_expenses_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expenses_purchases_purchase_id",
                        column: x => x.purchase_id,
                        principalTable: "purchases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_expenses_units_unit_id",
                        column: x => x.unit_id,
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "units",
                columns: new[] { "id", "code", "is_active", "kind", "name", "symbol" },
                values: new object[,]
                {
                    { 1L, "PCS", true, 0, "Piece", "pcs" },
                    { 2L, "PACK", true, 0, "Pack", "pack" },
                    { 3L, "BUNCH", true, 0, "Bunch", "bunch" },
                    { 4L, "KG", true, 1, "Kilogram", "kg" },
                    { 5L, "G", true, 1, "Gram", "g" },
                    { 6L, "L", true, 2, "Litre", "l" },
                    { 7L, "ML", true, 2, "Millilitre", "ml" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_categories_code",
                table: "categories",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_categories_parent_id",
                table: "categories",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_category_id",
                table: "expenses",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_description_trgm",
                table: "expenses",
                column: "description")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_expenses_purchase_id",
                table: "expenses",
                column: "purchase_id");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_unit_id",
                table: "expenses",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "IX_extraction_candidates_extraction_result_id",
                table: "extraction_candidates",
                column: "extraction_result_id");

            migrationBuilder.CreateIndex(
                name: "IX_extraction_results_receipt_image_id",
                table: "extraction_results",
                column: "receipt_image_id");

            migrationBuilder.CreateIndex(
                name: "ix_merchants_name_trgm",
                table: "merchants",
                column: "name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_merchants_parent_id",
                table: "merchants",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_merchants_tax_id",
                table: "merchants",
                column: "tax_id",
                unique: true,
                filter: "tax_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_purchases_merchant_id",
                table: "purchases",
                column: "merchant_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchases_merchant_raw_trgm",
                table: "purchases",
                column: "merchant_raw")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_purchases_occurred_at_amount",
                table: "purchases",
                columns: new[] { "occurred_at", "amount" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchases_receipt_image_id",
                table: "purchases",
                column: "receipt_image_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_images_content_hash",
                table: "receipt_images",
                column: "content_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_units_code",
                table: "units",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expenses");

            migrationBuilder.DropTable(
                name: "extraction_candidates");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "purchases");

            migrationBuilder.DropTable(
                name: "units");

            migrationBuilder.DropTable(
                name: "extraction_results");

            migrationBuilder.DropTable(
                name: "merchants");

            migrationBuilder.DropTable(
                name: "receipt_images");
        }
    }
}
