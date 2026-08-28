using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expenses.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PascalCaseIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_categories_categories_parent_id",
                table: "categories");

            migrationBuilder.DropForeignKey(
                name: "FK_expenses_categories_category_id",
                table: "expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_expenses_purchases_purchase_id",
                table: "expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_expenses_units_unit_id",
                table: "expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_merchants_merchants_parent_id",
                table: "merchants");

            migrationBuilder.DropForeignKey(
                name: "FK_purchases_merchants_merchant_id",
                table: "purchases");

            migrationBuilder.DropPrimaryKey(
                name: "PK_units",
                table: "units");

            migrationBuilder.DropCheckConstraint(
                name: "ck_units_name_length",
                table: "units");

            migrationBuilder.DropPrimaryKey(
                name: "PK_purchases",
                table: "purchases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_purchases_merchant_raw_length",
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

            migrationBuilder.DropPrimaryKey(
                name: "PK_merchants",
                table: "merchants");

            migrationBuilder.DropIndex(
                name: "ix_merchants_tax_id",
                table: "merchants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_merchants_name_length",
                table: "merchants");

            migrationBuilder.DropPrimaryKey(
                name: "PK_expenses",
                table: "expenses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_expenses_category_raw_length",
                table: "expenses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_expenses_description_length",
                table: "expenses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_expenses_unit_raw_length",
                table: "expenses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_categories",
                table: "categories");

            migrationBuilder.DropCheckConstraint(
                name: "ck_categories_name_length",
                table: "categories");

            migrationBuilder.RenameTable(
                name: "units",
                newName: "Units");

            migrationBuilder.RenameTable(
                name: "purchases",
                newName: "Purchases");

            migrationBuilder.RenameTable(
                name: "merchants",
                newName: "Merchants");

            migrationBuilder.RenameTable(
                name: "expenses",
                newName: "Expenses");

            migrationBuilder.RenameTable(
                name: "categories",
                newName: "Categories");

            migrationBuilder.RenameColumn(
                name: "symbol",
                table: "Units",
                newName: "Symbol");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Units",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "kind",
                table: "Units",
                newName: "Kind");

            migrationBuilder.RenameColumn(
                name: "code",
                table: "Units",
                newName: "Code");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Units",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "Units",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "amount",
                table: "Purchases",
                newName: "Amount");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Purchases",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "occurred_at",
                table: "Purchases",
                newName: "OccurredAt");

            migrationBuilder.RenameColumn(
                name: "merchant_raw",
                table: "Purchases",
                newName: "MerchantRaw");

            migrationBuilder.RenameColumn(
                name: "merchant_id",
                table: "Purchases",
                newName: "MerchantId");

            migrationBuilder.RenameColumn(
                name: "receipt_storage_key",
                table: "Purchases",
                newName: "ReceiptStorageKey");

            migrationBuilder.RenameColumn(
                name: "receipt_state",
                table: "Purchases",
                newName: "ReceiptState");

            migrationBuilder.RenameColumn(
                name: "receipt_size_in_bytes",
                table: "Purchases",
                newName: "ReceiptSizeInBytes");

            migrationBuilder.RenameColumn(
                name: "receipt_failure_reason",
                table: "Purchases",
                newName: "ReceiptFailureReason");

            migrationBuilder.RenameColumn(
                name: "receipt_content_type",
                table: "Purchases",
                newName: "ReceiptContentType");

            migrationBuilder.RenameColumn(
                name: "receipt_content_hash",
                table: "Purchases",
                newName: "ReceiptContentHash");

            migrationBuilder.RenameColumn(
                name: "fiscal_jikr_supplied",
                table: "Purchases",
                newName: "FiscalJikrSupplied");

            migrationBuilder.RenameColumn(
                name: "fiscal_jikr_extracted",
                table: "Purchases",
                newName: "FiscalJikrExtracted");

            migrationBuilder.RenameColumn(
                name: "fiscal_ikof_supplied",
                table: "Purchases",
                newName: "FiscalIkofSupplied");

            migrationBuilder.RenameColumn(
                name: "fiscal_ikof_extracted",
                table: "Purchases",
                newName: "FiscalIkofExtracted");

            migrationBuilder.RenameColumn(
                name: "fiscal_extracted_source",
                table: "Purchases",
                newName: "FiscalExtractedSource");

            migrationBuilder.RenameIndex(
                name: "IX_purchases_merchant_id",
                table: "Purchases",
                newName: "IX_Purchases_MerchantId");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Merchants",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Merchants",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "tax_id",
                table: "Merchants",
                newName: "TaxId");

            migrationBuilder.RenameColumn(
                name: "parent_id",
                table: "Merchants",
                newName: "ParentId");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "Merchants",
                newName: "IsActive");

            migrationBuilder.RenameIndex(
                name: "IX_merchants_parent_id",
                table: "Merchants",
                newName: "IX_Merchants_ParentId");

            migrationBuilder.RenameColumn(
                name: "quantity",
                table: "Expenses",
                newName: "Quantity");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "Expenses",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "amount",
                table: "Expenses",
                newName: "Amount");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Expenses",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "unit_raw",
                table: "Expenses",
                newName: "UnitRaw");

            migrationBuilder.RenameColumn(
                name: "unit_price",
                table: "Expenses",
                newName: "UnitPrice");

            migrationBuilder.RenameColumn(
                name: "unit_id",
                table: "Expenses",
                newName: "UnitId");

            migrationBuilder.RenameColumn(
                name: "list_unit_price",
                table: "Expenses",
                newName: "ListUnitPrice");

            migrationBuilder.RenameColumn(
                name: "discount_amount",
                table: "Expenses",
                newName: "DiscountAmount");

            migrationBuilder.RenameColumn(
                name: "category_raw",
                table: "Expenses",
                newName: "CategoryRaw");

            migrationBuilder.RenameColumn(
                name: "category_id",
                table: "Expenses",
                newName: "CategoryId");

            migrationBuilder.RenameColumn(
                name: "purchase_id",
                table: "Expenses",
                newName: "PurchaseId");

            migrationBuilder.RenameIndex(
                name: "IX_expenses_unit_id",
                table: "Expenses",
                newName: "IX_Expenses_UnitId");

            migrationBuilder.RenameIndex(
                name: "IX_expenses_purchase_id",
                table: "Expenses",
                newName: "IX_Expenses_PurchaseId");

            migrationBuilder.RenameIndex(
                name: "IX_expenses_category_id",
                table: "Expenses",
                newName: "IX_Expenses_CategoryId");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Categories",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "code",
                table: "Categories",
                newName: "Code");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Categories",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "parent_id",
                table: "Categories",
                newName: "ParentId");

            migrationBuilder.RenameColumn(
                name: "is_system",
                table: "Categories",
                newName: "IsSystem");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "Categories",
                newName: "IsActive");

            migrationBuilder.RenameIndex(
                name: "IX_categories_parent_id",
                table: "Categories",
                newName: "IX_Categories_ParentId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Units",
                table: "Units",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Purchases",
                table: "Purchases",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Merchants",
                table: "Merchants",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Expenses",
                table: "Expenses",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Categories",
                table: "Categories",
                column: "Id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_units_name_length",
                table: "Units",
                sql: "length(\"Name\") <= 128");

            migrationBuilder.AddCheckConstraint(
                name: "ck_purchases_merchant_raw_length",
                table: "Purchases",
                sql: "\"MerchantRaw\" IS NULL OR length(\"MerchantRaw\") <= 512");

            migrationBuilder.AddCheckConstraint(
                name: "ck_purchases_receipt_all_or_nothing",
                table: "Purchases",
                sql: "(\"ReceiptContentHash\" IS NULL AND \"ReceiptStorageKey\" IS NULL AND \"ReceiptContentType\" IS NULL\n    AND \"ReceiptSizeInBytes\" IS NULL AND \"ReceiptState\" IS NULL)\nOR (\"ReceiptContentHash\" IS NOT NULL AND \"ReceiptStorageKey\" IS NOT NULL\n    AND \"ReceiptContentType\" IS NOT NULL AND \"ReceiptSizeInBytes\" IS NOT NULL\n    AND \"ReceiptState\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_purchases_receipt_content_hash_length",
                table: "Purchases",
                sql: "\"ReceiptContentHash\" IS NULL OR length(\"ReceiptContentHash\") = 32");

            migrationBuilder.AddCheckConstraint(
                name: "ck_purchases_receipt_storage_key_length",
                table: "Purchases",
                sql: "\"ReceiptStorageKey\" IS NULL OR length(\"ReceiptStorageKey\") <= 256");

            migrationBuilder.CreateIndex(
                name: "ix_merchants_tax_id",
                table: "Merchants",
                column: "TaxId",
                unique: true,
                filter: "\"TaxId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_merchants_name_length",
                table: "Merchants",
                sql: "length(\"Name\") <= 256");

            migrationBuilder.AddCheckConstraint(
                name: "ck_expenses_category_raw_length",
                table: "Expenses",
                sql: "\"CategoryRaw\" IS NULL OR length(\"CategoryRaw\") <= 256");

            migrationBuilder.AddCheckConstraint(
                name: "ck_expenses_description_length",
                table: "Expenses",
                sql: "length(\"Description\") <= 512");

            migrationBuilder.AddCheckConstraint(
                name: "ck_expenses_unit_raw_length",
                table: "Expenses",
                sql: "\"UnitRaw\" IS NULL OR length(\"UnitRaw\") <= 128");

            migrationBuilder.AddCheckConstraint(
                name: "ck_categories_name_length",
                table: "Categories",
                sql: "length(\"Name\") <= 256");

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_Categories_ParentId",
                table: "Categories",
                column: "ParentId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Categories_CategoryId",
                table: "Expenses",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Purchases_PurchaseId",
                table: "Expenses",
                column: "PurchaseId",
                principalTable: "Purchases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Units_UnitId",
                table: "Expenses",
                column: "UnitId",
                principalTable: "Units",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Merchants_Merchants_ParentId",
                table: "Merchants",
                column: "ParentId",
                principalTable: "Merchants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Merchants_MerchantId",
                table: "Purchases",
                column: "MerchantId",
                principalTable: "Merchants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Categories_Categories_ParentId",
                table: "Categories");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Categories_CategoryId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Purchases_PurchaseId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Units_UnitId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Merchants_Merchants_ParentId",
                table: "Merchants");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Merchants_MerchantId",
                table: "Purchases");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Units",
                table: "Units");

            migrationBuilder.DropCheckConstraint(
                name: "ck_units_name_length",
                table: "Units");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Purchases",
                table: "Purchases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_purchases_merchant_raw_length",
                table: "Purchases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_purchases_receipt_all_or_nothing",
                table: "Purchases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_purchases_receipt_content_hash_length",
                table: "Purchases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_purchases_receipt_storage_key_length",
                table: "Purchases");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Merchants",
                table: "Merchants");

            migrationBuilder.DropIndex(
                name: "ix_merchants_tax_id",
                table: "Merchants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_merchants_name_length",
                table: "Merchants");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Expenses",
                table: "Expenses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_expenses_category_raw_length",
                table: "Expenses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_expenses_description_length",
                table: "Expenses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_expenses_unit_raw_length",
                table: "Expenses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Categories",
                table: "Categories");

            migrationBuilder.DropCheckConstraint(
                name: "ck_categories_name_length",
                table: "Categories");

            migrationBuilder.RenameTable(
                name: "Units",
                newName: "units");

            migrationBuilder.RenameTable(
                name: "Purchases",
                newName: "purchases");

            migrationBuilder.RenameTable(
                name: "Merchants",
                newName: "merchants");

            migrationBuilder.RenameTable(
                name: "Expenses",
                newName: "expenses");

            migrationBuilder.RenameTable(
                name: "Categories",
                newName: "categories");

            migrationBuilder.RenameColumn(
                name: "Symbol",
                table: "units",
                newName: "symbol");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "units",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Kind",
                table: "units",
                newName: "kind");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "units",
                newName: "code");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "units",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "units",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "Amount",
                table: "purchases",
                newName: "amount");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "purchases",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "OccurredAt",
                table: "purchases",
                newName: "occurred_at");

            migrationBuilder.RenameColumn(
                name: "MerchantRaw",
                table: "purchases",
                newName: "merchant_raw");

            migrationBuilder.RenameColumn(
                name: "MerchantId",
                table: "purchases",
                newName: "merchant_id");

            migrationBuilder.RenameColumn(
                name: "ReceiptStorageKey",
                table: "purchases",
                newName: "receipt_storage_key");

            migrationBuilder.RenameColumn(
                name: "ReceiptState",
                table: "purchases",
                newName: "receipt_state");

            migrationBuilder.RenameColumn(
                name: "ReceiptSizeInBytes",
                table: "purchases",
                newName: "receipt_size_in_bytes");

            migrationBuilder.RenameColumn(
                name: "ReceiptFailureReason",
                table: "purchases",
                newName: "receipt_failure_reason");

            migrationBuilder.RenameColumn(
                name: "ReceiptContentType",
                table: "purchases",
                newName: "receipt_content_type");

            migrationBuilder.RenameColumn(
                name: "ReceiptContentHash",
                table: "purchases",
                newName: "receipt_content_hash");

            migrationBuilder.RenameColumn(
                name: "FiscalJikrSupplied",
                table: "purchases",
                newName: "fiscal_jikr_supplied");

            migrationBuilder.RenameColumn(
                name: "FiscalJikrExtracted",
                table: "purchases",
                newName: "fiscal_jikr_extracted");

            migrationBuilder.RenameColumn(
                name: "FiscalIkofSupplied",
                table: "purchases",
                newName: "fiscal_ikof_supplied");

            migrationBuilder.RenameColumn(
                name: "FiscalIkofExtracted",
                table: "purchases",
                newName: "fiscal_ikof_extracted");

            migrationBuilder.RenameColumn(
                name: "FiscalExtractedSource",
                table: "purchases",
                newName: "fiscal_extracted_source");

            migrationBuilder.RenameIndex(
                name: "IX_Purchases_MerchantId",
                table: "purchases",
                newName: "IX_purchases_merchant_id");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "merchants",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "merchants",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TaxId",
                table: "merchants",
                newName: "tax_id");

            migrationBuilder.RenameColumn(
                name: "ParentId",
                table: "merchants",
                newName: "parent_id");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "merchants",
                newName: "is_active");

            migrationBuilder.RenameIndex(
                name: "IX_Merchants_ParentId",
                table: "merchants",
                newName: "IX_merchants_parent_id");

            migrationBuilder.RenameColumn(
                name: "Quantity",
                table: "expenses",
                newName: "quantity");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "expenses",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Amount",
                table: "expenses",
                newName: "amount");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "expenses",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UnitRaw",
                table: "expenses",
                newName: "unit_raw");

            migrationBuilder.RenameColumn(
                name: "UnitPrice",
                table: "expenses",
                newName: "unit_price");

            migrationBuilder.RenameColumn(
                name: "UnitId",
                table: "expenses",
                newName: "unit_id");

            migrationBuilder.RenameColumn(
                name: "ListUnitPrice",
                table: "expenses",
                newName: "list_unit_price");

            migrationBuilder.RenameColumn(
                name: "DiscountAmount",
                table: "expenses",
                newName: "discount_amount");

            migrationBuilder.RenameColumn(
                name: "CategoryRaw",
                table: "expenses",
                newName: "category_raw");

            migrationBuilder.RenameColumn(
                name: "CategoryId",
                table: "expenses",
                newName: "category_id");

            migrationBuilder.RenameColumn(
                name: "PurchaseId",
                table: "expenses",
                newName: "purchase_id");

            migrationBuilder.RenameIndex(
                name: "IX_Expenses_UnitId",
                table: "expenses",
                newName: "IX_expenses_unit_id");

            migrationBuilder.RenameIndex(
                name: "IX_Expenses_PurchaseId",
                table: "expenses",
                newName: "IX_expenses_purchase_id");

            migrationBuilder.RenameIndex(
                name: "IX_Expenses_CategoryId",
                table: "expenses",
                newName: "IX_expenses_category_id");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "categories",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "categories",
                newName: "code");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "categories",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "ParentId",
                table: "categories",
                newName: "parent_id");

            migrationBuilder.RenameColumn(
                name: "IsSystem",
                table: "categories",
                newName: "is_system");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "categories",
                newName: "is_active");

            migrationBuilder.RenameIndex(
                name: "IX_Categories_ParentId",
                table: "categories",
                newName: "IX_categories_parent_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_units",
                table: "units",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_purchases",
                table: "purchases",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_merchants",
                table: "merchants",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_expenses",
                table: "expenses",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_categories",
                table: "categories",
                column: "id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_units_name_length",
                table: "units",
                sql: "length(name) <= 128");

            migrationBuilder.AddCheckConstraint(
                name: "ck_purchases_merchant_raw_length",
                table: "purchases",
                sql: "merchant_raw IS NULL OR length(merchant_raw) <= 512");

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

            migrationBuilder.CreateIndex(
                name: "ix_merchants_tax_id",
                table: "merchants",
                column: "tax_id",
                unique: true,
                filter: "tax_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_merchants_name_length",
                table: "merchants",
                sql: "length(name) <= 256");

            migrationBuilder.AddCheckConstraint(
                name: "ck_expenses_category_raw_length",
                table: "expenses",
                sql: "category_raw IS NULL OR length(category_raw) <= 256");

            migrationBuilder.AddCheckConstraint(
                name: "ck_expenses_description_length",
                table: "expenses",
                sql: "length(description) <= 512");

            migrationBuilder.AddCheckConstraint(
                name: "ck_expenses_unit_raw_length",
                table: "expenses",
                sql: "unit_raw IS NULL OR length(unit_raw) <= 128");

            migrationBuilder.AddCheckConstraint(
                name: "ck_categories_name_length",
                table: "categories",
                sql: "length(name) <= 256");

            migrationBuilder.AddForeignKey(
                name: "FK_categories_categories_parent_id",
                table: "categories",
                column: "parent_id",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_expenses_categories_category_id",
                table: "expenses",
                column: "category_id",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_expenses_purchases_purchase_id",
                table: "expenses",
                column: "purchase_id",
                principalTable: "purchases",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_expenses_units_unit_id",
                table: "expenses",
                column: "unit_id",
                principalTable: "units",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_merchants_merchants_parent_id",
                table: "merchants",
                column: "parent_id",
                principalTable: "merchants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_purchases_merchants_merchant_id",
                table: "purchases",
                column: "merchant_id",
                principalTable: "merchants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
