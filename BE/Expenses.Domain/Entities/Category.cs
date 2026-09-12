namespace Expenses.Domain.Entities;

/// <summary>
/// A dictionary entry keyed by an immutable <see cref="Code"/> (D8). Seeded and user-extensible;
/// retired by <see cref="IsActive"/> rather than deleted, because deleting would orphan history.
/// </summary>
public sealed class Category
{
    private readonly List<Category> _children = [];

    private Category()
    {
        // EF materialisation.
        Code = null!;
        Name = null!;
    }

    public long Id { get; private set; }

    /// <summary>Stable, immutable identity — the seeding key and the MCP argument vocabulary (D8).</summary>
    public string Code { get; private set; }

    /// <summary>Display only; freely editable and never a reference (D8).</summary>
    public string Name { get; private set; }

    public long? ParentId { get; private set; }

    public Category? Parent { get; private set; }

    public IReadOnlyList<Category> Children => _children;

    /// <summary>Seeded entries are system-provided and cannot be deleted.</summary>
    public bool IsSystem { get; private set; }

    public bool IsActive { get; private set; }

    public static Category Create(string code, string name, Category? parent = null, bool isSystem = false)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("A category requires a code.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A category requires a display name.", nameof(name));
        }

        var category = new Category
        {
            Code = code.Trim(),
            Name = name.Trim(),
            IsSystem = isSystem,
            IsActive = true,
        };

        if (parent is not null)
        {
            category.SetParent(parent);
        }

        return category;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A category requires a display name.", nameof(name));
        }

        Name = name.Trim();
    }

    /// <summary>Rejects any assignment that would make this category a descendant of itself.</summary>
    public void SetParent(Category? parent)
    {
        if (parent is not null && WouldCycle(parent))
        {
            throw new InvalidOperationException(
                $"Making '{Code}' a child of '{parent.Code}' would create a cycle.");
        }

        Parent?._children.Remove(this);

        Parent = parent;
        ParentId = parent?.Id;
        parent?._children.Add(this);
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    private bool WouldCycle(Category candidate)
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
