namespace Expenses.Domain.Entities;

/// <summary>
/// Fixed reference data keyed by an immutable <see cref="Code"/> (D8). Not user-creatable in this
/// change; seeded via <c>HasData</c> because nobody edits it (D15).
/// </summary>
public sealed class Unit
{
    /// <summary>What a quantity counts. Nested because it is part of this entity, not a type of its own.</summary>
    public enum UnitKind
    {
        /// <summary>Discrete count — pieces, packs, bunches.</summary>
        Count = 0,

        Mass = 1,

        Volume = 2,
    }

    private Unit()
    {
        // EF materialisation.
        Code = null!;
        Name = null!;
        Symbol = null!;
    }

    public long Id { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string Symbol { get; private set; }

    public UnitKind Kind { get; private set; }

    public bool IsActive { get; private set; }

    public static Unit Create(string code, string name, string symbol, UnitKind kind)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("A unit requires a code.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A unit requires a display name.", nameof(name));
        }

        return new Unit
        {
            Code = code.Trim(),
            Name = name.Trim(),
            Symbol = symbol.Trim(),
            Kind = kind,
            IsActive = true,
        };
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
