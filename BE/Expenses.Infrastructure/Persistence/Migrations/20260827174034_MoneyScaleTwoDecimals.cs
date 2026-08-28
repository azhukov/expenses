using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expenses.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoneyScaleTwoDecimals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "amount",
                table: "purchases",
                type: "numeric(19,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "total",
                table: "extraction_results",
                type: "numeric(19,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "tax_amount",
                table: "extraction_results",
                type: "numeric(19,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "unit_price",
                table: "extraction_candidates",
                type: "numeric(19,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "list_unit_price",
                table: "extraction_candidates",
                type: "numeric(19,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "discount_amount",
                table: "extraction_candidates",
                type: "numeric(19,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "amount",
                table: "extraction_candidates",
                type: "numeric(19,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "unit_price",
                table: "expenses",
                type: "numeric(19,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "list_unit_price",
                table: "expenses",
                type: "numeric(19,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "discount_amount",
                table: "expenses",
                type: "numeric(19,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "amount",
                table: "expenses",
                type: "numeric(19,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "amount",
                table: "purchases",
                type: "numeric(19,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "total",
                table: "extraction_results",
                type: "numeric(19,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "tax_amount",
                table: "extraction_results",
                type: "numeric(19,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "unit_price",
                table: "extraction_candidates",
                type: "numeric(19,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "list_unit_price",
                table: "extraction_candidates",
                type: "numeric(19,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "discount_amount",
                table: "extraction_candidates",
                type: "numeric(19,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "amount",
                table: "extraction_candidates",
                type: "numeric(19,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "unit_price",
                table: "expenses",
                type: "numeric(19,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "list_unit_price",
                table: "expenses",
                type: "numeric(19,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "discount_amount",
                table: "expenses",
                type: "numeric(19,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "amount",
                table: "expenses",
                type: "numeric(19,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,2)");
        }
    }
}
