using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expenses.Infrastructure.Persistence.Migrations;

/// <summary>
/// Background extraction is removed: a receipt is now only ever constructed already in a
/// terminal state, and <c>Purchase.ExtractionState</c> no longer has <c>Pending</c> (0) or
/// <c>Extracting</c> (1) members. Nothing can resume the bytes-in-flight a receipt found in
/// either state was mid-queue on, so each is transitioned to <c>Failed</c> (4) with a reason
/// noting why, leaving it in a state a user can explicitly re-run. No schema change is needed:
/// <c>receipt_state</c> has always been a plain integer column with no check constraint tying it
/// to the C# enum's members.
/// </summary>
public partial class MigrateStrandedExtractionStates : Migration
{
    private const string StrandedReason = "stranded by removal of background extraction";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""
            UPDATE purchases
            SET receipt_state = 4, receipt_failure_reason = '{StrandedReason}'
            WHERE receipt_state IN (0, 1);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Irreversible: the bytes-in-flight a Pending or Extracting receipt was waiting on are
        // gone, so there is no run to resume by moving a row back to either state.
    }
}
