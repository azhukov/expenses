using Expenses.Domain;

namespace Expenses.Infrastructure.Persistence.Configurations;

/// <summary>
/// The fixed unit dictionary (D15). Identifiers are part of the seed because <c>HasData</c> owns
/// these rows: they are model-managed, and nobody edits them. Codes are the stable identity a
/// receipt line is matched to and an MCP argument names (D8); the verbatim text a receipt printed
/// is kept beside the match rather than expanding this list (D9).
/// </summary>
internal static class UnitSeed
{
    public static readonly object[] Rows =
    [
        Row(1, "PCS", "Piece", "pcs", Unit.UnitKind.Count),
        Row(2, "PACK", "Pack", "pack", Unit.UnitKind.Count),
        Row(3, "BUNCH", "Bunch", "bunch", Unit.UnitKind.Count),
        Row(4, "KG", "Kilogram", "kg", Unit.UnitKind.Mass),
        Row(5, "G", "Gram", "g", Unit.UnitKind.Mass),
        Row(6, "L", "Litre", "l", Unit.UnitKind.Volume),
        Row(7, "ML", "Millilitre", "ml", Unit.UnitKind.Volume),
    ];

    private static object Row(long id, string code, string name, string symbol, Unit.UnitKind kind)
        => new
        {
            Id = id,
            Code = code,
            Name = name,
            Symbol = symbol,
            Kind = kind,
            IsActive = true,
        };
}
