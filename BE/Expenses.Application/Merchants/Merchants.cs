using Expenses.Application.Abstractions;
using Expenses.Application.Errors;
using Expenses.Domain;

namespace Expenses.Application.Merchants;

/// <summary>
/// How an incoming merchant was reconciled with the dictionary, reported rather than inferred:
/// matching by tax number is a fact, matching by name is a guess, and creating one is neither (D18).
/// </summary>
public enum MerchantMatchKind
{
    MatchedByTaxId = 0,
    MatchedByName = 1,
    Created = 2,
}

public sealed record MerchantResolution(Merchant Merchant, MerchantMatchKind Kind)
{
    public bool NewlyAdded => Kind == MerchantMatchKind.Created;
}

/// <summary>
/// Matches by tax identification number where the receipt printed one, falls back to name where it
/// did not, and adds the merchant when it is unknown (D18).
/// </summary>
public sealed class ResolveMerchant(IMerchantRepository merchants, IUnitOfWork unitOfWork)
{
    public async Task<MerchantResolution> Execute(
        string name,
        string? taxId = null,
        CancellationToken cancellationToken = default)
    {
        var trimmedTaxId = string.IsNullOrWhiteSpace(taxId) ? null : taxId.Trim();

        if (trimmedTaxId is not null)
        {
            // The tax number is the identity, so a match on it settles the question whatever the
            // receipt called the shop — and a name-only entry never absorbs a fiscalised receipt,
            // because two different tax numbers are two different merchants (D18).
            if (await merchants.FindByTaxId(trimmedTaxId, cancellationToken) is { } known)
            {
                return new MerchantResolution(known, MerchantMatchKind.MatchedByTaxId);
            }
        }
        else if (await merchants.FindByName(name.Trim(), cancellationToken) is { } namedAlike)
        {
            // Never authoritative: the name on the paper is a brand, inconsistently abbreviated,
            // and the field OCR is most likely to mangle (D18).
            return new MerchantResolution(namedAlike, MerchantMatchKind.MatchedByName);
        }

        Merchant created;
        try
        {
            created = Merchant.Create(name, trimmedTaxId);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw DomainErrorTranslation.Merchant(exception);
        }

        // Saved here rather than with the purchase, because a dictionary entry is not part of the
        // purchase aggregate and the purchase needs its identifier in order to reference it (D18).
        await merchants.Add(created, cancellationToken);
        await unitOfWork.SaveChanges(cancellationToken);

        return new MerchantResolution(created, MerchantMatchKind.Created);
    }
}

public sealed class ListMerchants(IMerchantRepository merchants)
{
    public async Task<IReadOnlyList<MerchantView>> Execute(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var listed = await merchants.List(includeInactive, cancellationToken);

        return listed
            .OrderBy(merchant => merchant.Name, StringComparer.OrdinalIgnoreCase)
            .Select(MerchantView.Of)
            .ToList();
    }
}

/// <summary>
/// Searches the dictionary and the verbatim merchant text on purchases together, because an
/// unmatched merchant is exactly the one a user will search for by half-remembered name (D14).
/// </summary>
public sealed class SearchMerchants(IMerchantRepository merchants)
{
    public async Task<IReadOnlyList<MerchantMatchView>> Execute(
        string term,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            throw ExpensesException.For(
                ApplicationErrors.MerchantSearchTermRequired,
                "A merchant search needs something to search for.",
                ("term", term));
        }

        var matches = await merchants.Search(term.Trim(), cancellationToken);

        return matches.Select(MerchantMatchView.Of).ToList();
    }
}

public sealed class RenameMerchant(IMerchantRepository merchants, IUnitOfWork unitOfWork)
{
    public async Task<MerchantView> Execute(long id, string name, CancellationToken cancellationToken = default)
    {
        var merchant = await merchants.Require(id, cancellationToken);

        try
        {
            merchant.Rename(name);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw DomainErrorTranslation.Merchant(exception);
        }

        await unitOfWork.SaveChanges(cancellationToken);

        return MerchantView.Of(merchant);
    }
}

/// <summary>Records a branch beneath the chain it belongs to, rejecting a cycle (D18).</summary>
public sealed class SetMerchantParent(IMerchantRepository merchants, IUnitOfWork unitOfWork)
{
    public async Task<MerchantView> Execute(
        long id,
        long? parentId,
        CancellationToken cancellationToken = default)
    {
        var merchant = await merchants.Require(id, cancellationToken);
        var parent = parentId is { } wanted ? await merchants.Require(wanted, cancellationToken) : null;

        try
        {
            merchant.SetParent(parent);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw DomainErrorTranslation.Merchant(exception);
        }

        await unitOfWork.SaveChanges(cancellationToken);

        return MerchantView.Of(merchant);
    }
}

/// <summary>
/// Retires a merchant rather than deleting it, so purchases that reference it stay readable. A
/// merchant with active children is refused, and the refusal names them (D18).
/// </summary>
public sealed class DeactivateMerchant(IMerchantRepository merchants, IUnitOfWork unitOfWork)
{
    public async Task<MerchantView> Execute(long id, CancellationToken cancellationToken = default)
    {
        var merchant = await merchants.Require(id, cancellationToken);
        var children = await merchants.ActiveChildrenOf(id, cancellationToken);

        if (children.Count > 0)
        {
            throw ExpensesException.For(
                ApplicationErrors.MerchantHasActiveChildren,
                $"'{merchant.Name}' still has active branches; deactivate them first.",
                ("children", children.Select(child => child.Name).ToList()));
        }

        merchant.Deactivate();
        await unitOfWork.SaveChanges(cancellationToken);

        return MerchantView.Of(merchant);
    }
}

internal static class MerchantLookup
{
    public static async Task<Merchant> Require(
        this IMerchantRepository merchants,
        long id,
        CancellationToken cancellationToken) =>
        await merchants.FindById(id, cancellationToken)
        ?? throw ExpensesException.For(
            ApplicationErrors.MerchantNotFound,
            $"There is no merchant with identifier {id}.",
            ("id", id));
}
