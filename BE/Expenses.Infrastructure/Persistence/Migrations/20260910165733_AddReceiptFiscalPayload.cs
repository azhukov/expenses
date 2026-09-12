using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expenses.Infrastructure.Persistence.Migrations;

/// <summary>
/// The fiscal QR payload is retained verbatim beside the identifiers parsed out of it (D32).
///
/// It is kept for re-runs above all: decoding a stored photograph reads one symbol in three, so a
/// receipt whose code a client read at capture would lose that reading on every later extraction if
/// only the parsed identifiers survived. Keeping the payload also leaves an unrecognised format
/// recoverable — the portal's response shape was observed from exactly one invoice, so learning a
/// field later is the expected case, and a discarded payload cannot be re-read.
///
/// Both columns are additive and nullable, so reverting the code that reads them leaves them unread
/// rather than requiring this migration to be undone.
/// </summary>
public partial class AddReceiptFiscalPayload : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "fiscal_payload",
            table: "purchases",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "fiscal_payload_source",
            table: "purchases",
            type: "integer",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "fiscal_payload",
            table: "purchases");

        migrationBuilder.DropColumn(
            name: "fiscal_payload_source",
            table: "purchases");
    }
}
