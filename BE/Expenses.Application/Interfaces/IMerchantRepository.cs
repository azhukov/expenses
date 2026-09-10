using Expenses.Domain.Entities;

namespace Expenses.Application.Interfaces;

/// <summary>
/// A dictionary learned during ingestion rather than seeded (D18), so unlike categories and units
/// it is looked up by tax identification number first and by name only as a fallback.
/// </summary>
public interface IMerchantRepository
{
    Task<Merchant?> FindById(long id, CancellationToken cancellationToken = default);

    /// <summary>The authoritative match, where the receipt printed a tax number (D18).</summary>
    Task<Merchant?> FindByTaxId(string taxId, CancellationToken cancellationToken = default);

    /// <summary>The fallback match. Never authoritative — the name on the paper is a brand (D18).</summary>
    Task<Merchant?> FindByName(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Merchant>> List(bool includeInactive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Accent- and typo-tolerant search over both the dictionary name and the verbatim merchant
    /// text retained on purchases (D14), since an unmatched merchant is exactly the one a user
    /// will search for by half-remembered name.
    /// </summary>
    Task<IReadOnlyList<MerchantMatch>> Search(string term, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Merchant>> ActiveChildrenOf(long merchantId, CancellationToken cancellationToken = default);

    Task Add(Merchant merchant, CancellationToken cancellationToken = default);
}

/// <summary>
/// A search hit. <paramref name="Merchant"/> is null when the term matched only verbatim text on a
/// purchase, which is a hit worth reporting rather than one to hide.
/// </summary>
public sealed record MerchantMatch(Merchant? Merchant, string MatchedText, long? PurchaseId = null);
