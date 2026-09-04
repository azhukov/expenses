using Expenses.Application.Abstractions;
using Expenses.Application.Errors;
using Expenses.Domain;

namespace Expenses.Application.ReferenceData;

/// <summary>
/// A rename carries the code it is renaming. A <paramref name="RequestedCode"/> that differs from
/// <paramref name="Code"/> is a code change, which is rejected rather than quietly ignored (D8).
/// </summary>
public sealed record RenameCategoryCommand(string Code, string Name, string? RequestedCode = null);

public sealed record CreateCategoryCommand(string Code, string Name, string? ParentCode = null);

/// <summary>
/// Active entries by default, in a stable display order, so two listings with no intervening
/// change agree. Ordering by code rather than by name keeps it stable across a rename.
/// </summary>
public sealed class ListCategories(ICategoryRepository categories)
{
    public async Task<IReadOnlyList<CategoryView>> Execute(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var listed = await categories.List(includeInactive, cancellationToken);

        return [.. listed
            .OrderBy(category => category.Code, StringComparer.Ordinal)
            .Select(CategoryView.Of)];
    }
}

public sealed class CreateCategory(ICategoryRepository categories, IUnitOfWork unitOfWork)
{
    public async Task<CategoryView> Execute(
        CreateCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        if (await categories.FindByCode(command.Code, cancellationToken) is not null)
        {
            throw ExpensesException.For(
                ApplicationErrors.CategoryDuplicateCode,
                $"A category with code '{command.Code}' already exists.",
                ("code", command.Code));
        }

        var parent = command.ParentCode is { } parentCode
            ? await categories.Require(parentCode, cancellationToken)
            : null;

        Category created;
        try
        {
            created = Category.Create(command.Code, command.Name, parent);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw DomainErrorTranslation.Category(exception);
        }

        await categories.Add(created, cancellationToken);
        await unitOfWork.SaveChanges(cancellationToken);

        return CategoryView.Of(created);
    }
}

public sealed class RenameCategory(ICategoryRepository categories, IUnitOfWork unitOfWork)
{
    public async Task<CategoryView> Execute(
        RenameCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        var category = await categories.Require(command.Code, cancellationToken);

        // A code is the stable identity that survives a rename and that stored references point
        // at (D8), so a request to change one is refused rather than silently dropped.
        if (command.RequestedCode is { } requested
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
            category.Rename(command.Name);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw DomainErrorTranslation.Category(exception);
        }

        await unitOfWork.SaveChanges(cancellationToken);

        return CategoryView.Of(category);
    }
}

/// <summary>
/// Retirement rather than deletion, because deleting would orphan or rewrite history (D8). A
/// category with active children is refused, and the refusal names the blocking children.
/// </summary>
public sealed class DeactivateCategory(ICategoryRepository categories, IUnitOfWork unitOfWork)
{
    public async Task<CategoryView> Execute(string code, CancellationToken cancellationToken = default)
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
}

/// <summary>
/// Seeded entries are system-provided and cannot be deleted (D8); a user-created one can be, since
/// nothing seeded depends on it. Retirement remains the way to withdraw a category that history
/// references — deletion is for an entry created by mistake.
/// </summary>
public sealed class DeleteCategory(ICategoryRepository categories, IUnitOfWork unitOfWork)
{
    public async Task Execute(string code, CancellationToken cancellationToken = default)
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
}

internal static class CategoryLookup
{
    public static async Task<Category> Require(
        this ICategoryRepository categories,
        string code,
        CancellationToken cancellationToken)
        => await categories.FindByCode(code, cancellationToken)
        ?? throw ExpensesException.For(
            ApplicationErrors.CategoryNotFound,
            $"There is no category with code '{code}'.",
            ("code", code));
}
