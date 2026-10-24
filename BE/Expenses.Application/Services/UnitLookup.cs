using Expenses.Application.Interfaces;

namespace Expenses.Application.Services;

/// <summary>
/// What printed unit text means, loaded whole once per operation: the dictionary is a handful of
/// units and a few dozen aliases, so one read beats a query per line. Everything is compared in
/// <see cref="UnitText"/>'s normalised form.
/// </summary>
internal sealed class UnitLookup
{
    /// <summary>Normalised code or alias of an active unit, to that unit.</summary>
    private readonly Dictionary<string, long> _matchable;

    /// <summary>Every normalised code and alias, of active and inactive units alike.</summary>
    private readonly HashSet<string> _known;

    private UnitLookup(Dictionary<string, long> matchable, HashSet<string> known)
    {
        _matchable = matchable;
        _known = known;
    }

    public static async Task<UnitLookup> Load(
        IUnitRepository units,
        IUnitAliasRepository aliases,
        CancellationToken cancellationToken)
    {
        var allUnits = await units.List(includeInactive: true, cancellationToken);
        var allAliases = await aliases.List(cancellationToken);

        var active = allUnits.Where(unit => unit.IsActive).Select(unit => unit.Id).ToHashSet();
        var matchable = new Dictionary<string, long>(StringComparer.Ordinal);
        var known = new HashSet<string>(StringComparer.Ordinal);

        // Aliases first, then codes over them, so where a stray alias spells a code the code wins.
        foreach (var alias in allAliases)
        {
            known.Add(alias.Alias);

            if (active.Contains(alias.UnitId))
            {
                matchable[alias.Alias] = alias.UnitId;
            }
        }

        foreach (var unit in allUnits)
        {
            string code = UnitText.Normalise(unit.Code)!;
            known.Add(code);

            if (unit.IsActive)
            {
                matchable[code] = unit.Id;
            }
        }

        return new UnitLookup(matchable, known);
    }

    /// <summary>The active unit the printed text names, by code first and then by alias; null for none.</summary>
    public long? Match(string? printed)
        => UnitText.Normalise(printed) is { } normalised && _matchable.TryGetValue(normalised, out long unitId)
            ? unitId
            : null;

    /// <summary>
    /// Whether the printed text is any unit's code or any alias, active or not. Known text is never
    /// learned as a new alias, which is what keeps learning from overriding anything.
    /// </summary>
    public bool IsKnown(string? printed)
        => UnitText.Normalise(printed) is { } normalised && _known.Contains(normalised);

    /// <summary>
    /// Records an alias learned during this operation, so a later line in the same save sees it and
    /// the earlier line decides.
    /// </summary>
    public void Remember(string normalised, long unitId)
    {
        _known.Add(normalised);
        _matchable.TryAdd(normalised, unitId);
    }
}
