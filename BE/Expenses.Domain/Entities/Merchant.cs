namespace Expenses.Domain.Entities;

/// <summary>
/// A dictionary learned during ingestion rather than seeded (D18). Deliberately unlike
/// <see cref="Category"/> and <see cref="Unit"/>: no <c>code</c>, no <c>is_system</c>, and
/// identity comes from <see cref="TaxId"/> where the receipt printed one — nothing where it did not.
/// </summary>
public sealed class Merchant
{
    private readonly List<Merchant> _children = [];

    private Merchant()
    {
        // EF materialisation.
        Name = null!;
    }

    public long Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>Tax identification number. Unique when present; absent for an unfiscalised seller.</summary>
    public string? TaxId { get; private set; }

    public long? ParentId { get; private set; }

    /// <summary>The chain a branch belongs to.</summary>
    public Merchant? Parent { get; private set; }

    public IReadOnlyList<Merchant> Children => _children;

    public bool IsActive { get; private set; }

    public static Merchant Create(string name, string? taxId = null, Merchant? parent = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A merchant requires a name.", nameof(name));
        }

        var merchant = new Merchant
        {
            Name = name.Trim(),
            TaxId = string.IsNullOrWhiteSpace(taxId) ? null : taxId.Trim(),
            IsActive = true,
        };

        if (parent is not null)
        {
            merchant.SetParent(parent);
        }

        return merchant;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A merchant requires a name.", nameof(name));
        }

        Name = name.Trim();
    }

    /// <summary>Rejects any assignment that would make this merchant a descendant of itself.</summary>
    public void SetParent(Merchant? parent)
    {
        if (parent is not null && WouldCycle(parent))
        {
            throw new InvalidOperationException(
                $"Making '{Name}' a child of '{parent.Name}' would create a cycle.");
        }

        Parent?._children.Remove(this);

        Parent = parent;
        ParentId = parent?.Id;
        parent?._children.Add(this);
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    private bool WouldCycle(Merchant candidate)
    {
        for (var ancestor = candidate; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, this))
            {
                return true;
            }
        }

        return false;
    }
}
