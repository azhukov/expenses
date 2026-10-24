using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Expenses.Infrastructure.Persistence.Migrations;

/// <summary>
/// The spellings a receipt prints for a unit, so printed text can be matched to the fixed unit
/// dictionary (D9) without the dictionary itself changing (D15).
///
/// The starting aliases are inserted here, once, and nowhere else. Startup seeding runs on every
/// start and would bring back an alias the user removed; <c>HasData</c> would make the model own
/// rows the user edits. A migration runs once per database, which is exactly the property wanted.
/// None of them repeats a unit's code — a code already matches on its own. Unit identifiers are
/// the fixed ones <c>UnitSeed</c> assigns.
///
/// Rollback: <see cref="Down"/> drops the table. Expenses matched through an alias keep their unit
/// and their printed text, which are ordinary columns of their own.
/// </summary>
public partial class AddUnitAliases : Migration
{
    private const long Piece = 1;
    private const long Pack = 2;
    private const long Kilogram = 4;
    private const long Gram = 5;
    private const long Litre = 6;

    private static readonly (string Alias, long UnitId)[] s_starting =
    [
        ("kom", Piece), ("kd", Piece), ("kos", Piece), ("stk", Piece), ("st", Piece), ("pc", Piece),
        ("kgr", Kilogram),
        ("gr", Gram),
        ("lit", Litre), ("ltr", Litre),
        ("pak", Pack), ("pkg", Pack),
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "unit_aliases",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                unit_id = table.Column<long>(type: "bigint", nullable: false),
                alias = table.Column<string>(type: "text", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_unit_aliases", x => x.id);
                table.ForeignKey(
                    name: "FK_unit_aliases_units_unit_id",
                    column: x => x.unit_id,
                    principalTable: "units",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_unit_aliases_alias",
            table: "unit_aliases",
            column: "alias",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_unit_aliases_unit_id",
            table: "unit_aliases",
            column: "unit_id");

        foreach (var (alias, unitId) in s_starting)
        {
            migrationBuilder.InsertData(
                table: "unit_aliases",
                columns: ["alias", "unit_id"],
                values: [alias, unitId]);
        }
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "unit_aliases");
    }
}
