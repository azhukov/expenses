using Expenses.Application.Dtos;
using Expenses.Application.Errors;
using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;

namespace Expenses.Application.Services;

/// <summary>The category dictionary, seeded and user-extended (D8).</summary>
public sealed class CategoryService(ICategoryRepository categories, IUnitOfWork unitOfWork)
{
    public async Task<CategoryView> Create(
        string code,
        string name,
        string? parentCode = null,
        CancellationToken cancellationToken = default)
    {
        if (await categories.FindByCode(code, cancellationToken) is not null)
        {
            throw ExpensesException.For(
                ApplicationErrors.CategoryDuplicateCode,
                $"A category with code '{code}' already exists.",
                ("code", code));
        }

        var parent = parentCode is { } wanted ? await categories.Require(wanted, cancellationToken) : null;

        Category created;
        try
        {
            created = Category.Create(code, name, parent);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw DomainErrorTranslation.Category(exception);
        }

        await categories.Add(created, cancellationToken);
        await unitOfWork.SaveChanges(cancellationToken);

        return CategoryView.Of(created);
    }

    /// <summary>
    /// A code is the stable identity that survives a rename and that stored references point at
    /// (D8), so a <paramref name="requestedCode"/> that differs from <paramref name="code"/> is a
    /// code change, which is rejected rather than quietly ignored.
    /// </summary>
    public async Task<CategoryView> Rename(
        string code,
        string name,
        string? requestedCode = null,
        CancellationToken cancellationToken = default)
    {
        var category = await categories.Require(code, cancellationToken);

        if (requestedCode is { } requested
            && !string.Equals(requested, category.Code, StringComparison.Ordinal))
        {
            throw ExpensesException.For(
                ApplicationErrors.CategoryCodeImmutable,
                $"The code of a category cannot be changed; '{category.Code}' stays as it is.",
                ("code", category.Code),
                ("requestedCode", requested));
        }

        try
        {
            category.Rename(name);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw DomainErrorTranslation.Category(exception);
        }

        await unitOfWork.SaveChanges(cancellationToken);

        return CategoryView.Of(category);
    }

    /// <summary>
    /// Retirement rather than deletion, because deleting would orphan or rewrite history (D8). A
    /// category with active children is refused, and the refusal names the blocking children.
    /// </summary>
    public async Task<CategoryView> Deactivate(string code, CancellationToken cancellationToken = default)
    {
        var category = await categories.Require(code, cancellationToken);
        var children = await categories.ActiveChildrenOf(category.Id, cancellationToken);

        if (children.Count > 0)
        {
            throw ExpensesException.For(
                ApplicationErrors.CategoryHasActiveChildren,
                $"'{category.Code}' still has active children; deactivate them first.",
                ("code", category.Code),
                ("children", children.Select(child => child.Code).ToList()));
        }

        category.Deactivate();
        await unitOfWork.SaveChanges(cancellationToken);

        return CategoryView.Of(category);
    }

    /// <summary>
    /// Seeded entries are system-provided and cannot be deleted (D8); a user-created one can be, since
    /// nothing seeded depends on it. Retirement remains the way to withdraw a category that history
    /// references вЂ” deletion is for an entry created by mistake.
    /// </summary>
    public async Task Delete(string code, CancellationToken cancellationToken = default)
    {
        var category = await categories.Require(code, cancellationToken);

        if (category.IsSystem)
        {
            throw ExpensesException.For(
                ApplicationErrors.CategorySystemUndeletable,
                $"'{category.Code}' is system-provided and cannot be deleted; deactivate it instead.",
                ("code", category.Code));
        }

        var children = await categories.ActiveChildrenOf(category.Id, cancellationToken);
        if (children.Count > 0)
        {
            throw ExpensesException.For(
                ApplicationErrors.CategoryHasActiveChildren,
                $"'{category.Code}' still has active children and cannot be deleted.",
                ("code", category.Code),
                ("children", children.Select(child => child.Code).ToList()));
        }

        await categories.Remove(category, cancellationToken);
        await unitOfWork.SaveChanges(cancellationToken);
    }

    /// <summary>
    /// Active entries by default, in a stable display order, so two listings with no intervening
    /// change agree. Ordering by code rather than by name keeps it stable across a rename.
    /// </summary>
    public async Task<IReadOnlyList<CategoryView>> List(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var listed = await categories.List(includeInactive, cancellationToken);

        return [.. listed
            .OrderBy(category => category.Code, StringComparer.Ordinal)
            .Select(CategoryView.Of)];
    }
}
