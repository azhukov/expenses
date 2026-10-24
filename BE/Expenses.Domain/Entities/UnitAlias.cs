namespace Expenses.Domain.Entities;

/// <summary>
/// One spelling a receipt may print for a unit. The unit itself stays fixed (D15); its aliases are
/// the editable part, so a new spelling is a row here rather than a change to the dictionary. The
/// alias is held already normalised: normalising is an application rule, and the entity only
/// refuses what could never be one.
/// </summary>
public sealed class UnitAlias
{
    private UnitAlias()
    {
        // EF materialisation.
        Alias = null!;
    }

    public long Id { get; private set; }

    public long UnitId { get; private set; }

    public string Alias { get; private set; }

    public static UnitAlias Create(long unitId, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new ArgumentException("A unit alias requires text.", nameof(alias));
        }

        return new UnitAlias
        {
            UnitId = unitId,
            Alias = alias,
        };
    }
}
