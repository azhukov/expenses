using System.Security.Cryptography;
using Expenses.Application.Dtos;
using Expenses.Application.Errors;
using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;
using Expenses.Domain.Extraction;

namespace Expenses.Application.Tests.Fakes;

/// <summary>
/// One fake behind every port, so a use-case test reads as an arrangement of the ledger rather
/// than of six mocks. It is deliberately not a stub: the rules the real database enforces and the
/// use cases depend on are enforced here too — identity assignment, the unique index on
/// <c>(occurred_at, amount)</c> (D4), and content-hash reuse (D11) — because a fake that does not
/// enforce them lets a use case pass while the database rejects it.
/// </summary>
internal sealed class InMemoryLedger :
    IPurchaseRepository,
    ICategoryRepository,
    IUnitRepository,
    IMerchantRepository,
    IReceiptImageStore,
    ITemporaryReceiptStore,
    IExtractionCandidateStore,
    IUnitOfWork
{
    private readonly List<Purchase> _purchases = [];
    private readonly List<Purchase> _uncommittedPurchases = [];
    private readonly List<Category> _categories = [];
    private readonly List<Category> _uncommittedCategories = [];
    private readonly List<Unit> _units = [];
    private readonly List<Merchant> _merchants = [];
    private readonly List<Merchant> _uncommittedMerchants = [];
    private readonly Dictionary<string, byte[]> _files = [];
    private readonly Dictionary<Guid, (byte[] Content, DateTimeOffset WrittenAt)> _tempFiles = [];

    private readonly Dictionary<long, ExtractionResult> _results = [];
    private readonly List<Category> _removedCategories = [];

    private long _nextId;

    public int SaveCount { get; private set; }

    /// <summary>
    /// A purchase another writer committed between the guard's query and its insert — the case the
    /// unique index exists for (D4). It is committed by the next save, which then rejects.
    /// </summary>
    public Purchase? ConcurrentWinner { get; set; }

    public IReadOnlyList<Purchase> Purchases => _purchases;

    public IReadOnlyList<Merchant> Merchants => _merchants;

    public IReadOnlyList<Category> Categories => _categories;

    /// <summary>The files the store holds, by storage key.</summary>
    public IReadOnlyDictionary<string, byte[]> Files => _files;

    public IReadOnlyList<Category> RemovedCategories => _removedCategories;

    public Category Given(Category category)
    {
        _categories.Add(category.WithId(++_nextId));
        return category;
    }

    public Unit Given(Unit unit)
    {
        _units.Add(unit.WithId(++_nextId));
        return unit;
    }

    public Merchant Given(Merchant merchant)
    {
        _merchants.Add(merchant.WithId(++_nextId));
        return merchant;
    }

    public Purchase Given(Purchase purchase)
    {
        _purchases.Add(purchase.WithId(++_nextId));
        return purchase;
    }

    public ExtractionResult Given(long purchaseId, ExtractionResult result)
    {
        _results[purchaseId] = result;
        return result;
    }

    /// <summary>Writes bytes to the permanent store directly, as if a capture had already promoted them.</summary>
    public StoredReceiptFile GivenReceiptFile(byte[] content)
    {
        string contentType = ContentTypeOf(content);
        string storageKey = Convert.ToHexStringLower(SHA256.HashData(content));

        _files[storageKey] = content;

        return new StoredReceiptFile(storageKey, contentType, content.LongLength);
    }

    /// <summary>Seeds a temporary capture directly, as if a prior call to capture had produced it.</summary>
    public Guid GivenTemporaryCapture(byte[] content, DateTimeOffset? writtenAt = null)
    {
        var key = Guid.NewGuid();
        _tempFiles[key] = (content, writtenAt ?? DateTimeOffset.UtcNow);

        return key;
    }

    public bool HasTemporaryCapture(Guid key) => _tempFiles.ContainsKey(key);

    // ---- IUnitOfWork -------------------------------------------------------

    public Task SaveChanges(CancellationToken cancellationToken = default)
    {
        SaveCount++;

        if (ConcurrentWinner is { } winner)
        {
            ConcurrentWinner = null;
            _purchases.Add(winner.WithId(++_nextId));
        }

        foreach (var purchase in _uncommittedPurchases)
        {
            if (_purchases.Any(existing =>
                existing.OccurredAt == purchase.OccurredAt && existing.Amount == purchase.Amount))
            {
                _uncommittedPurchases.Clear();
                throw new DuplicatePurchaseException(purchase.OccurredAt, purchase.Amount);
            }

            _purchases.Add(purchase.WithId(++_nextId));
        }

        _uncommittedPurchases.Clear();

        foreach (var category in _uncommittedCategories)
        {
            _categories.Add(category.WithId(++_nextId));
        }

        _uncommittedCategories.Clear();

        foreach (var merchant in _uncommittedMerchants)
        {
            _merchants.Add(merchant.WithId(++_nextId));
        }

        _uncommittedMerchants.Clear();

        return Task.CompletedTask;
    }

    // ---- IPurchaseRepository ----------------------------------------------

    public Task<Purchase?> FindById(long id, CancellationToken cancellationToken = default)
        => Task.FromResult(_purchases.SingleOrDefault(purchase => purchase.Id == id));

    public Task<Purchase?> FindByOccurrenceAndAmount(
        DateTime occurredAt,
        decimal amount,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_purchases.FirstOrDefault(purchase =>
            purchase.OccurredAt == occurredAt && purchase.Amount == amount));

    /// <summary>
    /// Byte-identical receipts share one file, so this is what stops one purchase's deletion
    /// removing a file another is still showing (D11).
    /// </summary>
    public Task<int> CountByReceiptStorageKey(string storageKey, CancellationToken cancellationToken = default)
        => Task.FromResult(_purchases.Count(purchase =>
            purchase.Receipt is { } receipt && receipt.StorageKey == storageKey));

    public Task<IReadOnlyList<Purchase>> List(
        PurchaseListQuery query,
        CancellationToken cancellationToken = default)
    {
        var matching = _purchases
            .Where(purchase => query.From is not { } from
                || purchase.OccurredAt >= from.ToDateTime(TimeOnly.MinValue))
            .Where(purchase => query.To is not { } to
                || purchase.OccurredAt < to.AddDays(1).ToDateTime(TimeOnly.MinValue))
            .OrderByDescending(purchase => purchase.OccurredAt)
            .ThenByDescending(purchase => purchase.Id)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToList();

        return Task.FromResult<IReadOnlyList<Purchase>>(matching);
    }

    public Task Add(Purchase purchase, CancellationToken cancellationToken = default)
    {
        _uncommittedPurchases.Add(purchase);
        return Task.CompletedTask;
    }

    // ---- ICategoryRepository ----------------------------------------------

    Task<Category?> ICategoryRepository.FindById(long id, CancellationToken cancellationToken)
        => Task.FromResult(_categories.FirstOrDefault(category => category.Id == id));

    Task<Category?> ICategoryRepository.FindByCode(string code, CancellationToken cancellationToken)
        => Task.FromResult(_categories.FirstOrDefault(category =>
            string.Equals(category.Code, code, StringComparison.OrdinalIgnoreCase)));

    Task<IReadOnlyList<Category>> ICategoryRepository.List(
        bool includeInactive,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Category>>([.. _categories.Where(category => includeInactive || category.IsActive)]);

    Task<IReadOnlyList<Category>> ICategoryRepository.ActiveChildrenOf(
        long categoryId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Category>>([.. _categories.Where(category => category.Parent?.Id == categoryId && category.IsActive)]);

    Task ICategoryRepository.Add(Category category, CancellationToken cancellationToken)
    {
        _uncommittedCategories.Add(category);
        return Task.CompletedTask;
    }

    Task ICategoryRepository.Remove(Category category, CancellationToken cancellationToken)
    {
        _categories.Remove(category);
        _removedCategories.Add(category);
        return Task.CompletedTask;
    }

    // ---- IUnitRepository ---------------------------------------------------

    Task<Unit?> IUnitRepository.FindById(long id, CancellationToken cancellationToken)
        => Task.FromResult(_units.FirstOrDefault(unit => unit.Id == id));

    Task<Unit?> IUnitRepository.FindByCode(string code, CancellationToken cancellationToken)
        => Task.FromResult(_units.FirstOrDefault(unit =>
            string.Equals(unit.Code, code, StringComparison.OrdinalIgnoreCase)));

    Task<IReadOnlyList<Unit>> IUnitRepository.List(bool includeInactive, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Unit>>([.. _units.Where(unit => includeInactive || unit.IsActive)]);

    // ---- IMerchantRepository -----------------------------------------------

    Task<Merchant?> IMerchantRepository.FindById(long id, CancellationToken cancellationToken)
        => Task.FromResult(_merchants.FirstOrDefault(merchant => merchant.Id == id));

    public Task<Merchant?> FindByTaxId(string taxId, CancellationToken cancellationToken = default)
        => Task.FromResult(_merchants.FirstOrDefault(merchant =>
            merchant.TaxId is not null && string.Equals(merchant.TaxId, taxId, StringComparison.Ordinal)));

    public Task<Merchant?> FindByName(string name, CancellationToken cancellationToken = default)
        => Task.FromResult(_merchants.FirstOrDefault(merchant =>
            string.Equals(merchant.Name, name, StringComparison.OrdinalIgnoreCase)));

    Task<IReadOnlyList<Merchant>> IMerchantRepository.List(
        bool includeInactive,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Merchant>>([.. _merchants.Where(merchant => includeInactive || merchant.IsActive)]);

    public Task<IReadOnlyList<MerchantMatch>> Search(string term, CancellationToken cancellationToken = default)
    {
        var matches = _merchants
            .Where(merchant => merchant.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Select(merchant => new MerchantMatch(merchant, merchant.Name))
            .ToList();

        matches.AddRange(_purchases
            .Where(purchase => purchase.MerchantId is null
                && purchase.MerchantRaw is not null
                && purchase.MerchantRaw.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Select(purchase => new MerchantMatch(null, purchase.MerchantRaw!, purchase.Id)));

        return Task.FromResult<IReadOnlyList<MerchantMatch>>(matches);
    }

    Task<IReadOnlyList<Merchant>> IMerchantRepository.ActiveChildrenOf(
        long merchantId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Merchant>>([.. _merchants.Where(merchant => merchant.Parent?.Id == merchantId && merchant.IsActive)]);

    Task IMerchantRepository.Add(Merchant merchant, CancellationToken cancellationToken)
    {
        _uncommittedMerchants.Add(merchant);
        return Task.CompletedTask;
    }

    // ---- IReceiptImageStore ------------------------------------------------

    /// <summary>
    /// Content-addressed like the real store, so identical bytes are one file here too and the
    /// deduplication a use case relies on is the deduplication it will meet (D11).
    /// </summary>
    public Task<StoredReceiptFile> Save(byte[] content, CancellationToken cancellationToken = default)
    {
        string contentType = ContentTypeOf(content);
        string storageKey = Convert.ToHexStringLower(SHA256.HashData(content));

        _files[storageKey] = content;

        return Task.FromResult(new StoredReceiptFile(storageKey, contentType, content.LongLength));
    }

    public Task<byte[]?> Read(string storageKey, CancellationToken cancellationToken = default)
        => Task.FromResult(_files.GetValueOrDefault(storageKey));

    public Task Delete(string storageKey, CancellationToken cancellationToken = default)
    {
        _files.Remove(storageKey);
        return Task.CompletedTask;
    }

    // ---- IExtractionCandidateStore ----------------------------------------

    public Task<ExtractionResult?> FindLatest(long purchaseId, CancellationToken cancellationToken = default)
        => Task.FromResult(_results.GetValueOrDefault(purchaseId));

    public Task Replace(long purchaseId, ExtractionResult result, CancellationToken cancellationToken = default)
    {
        _results[purchaseId] = result;
        return Task.CompletedTask;
    }

    public Task Discard(long purchaseId, CancellationToken cancellationToken = default)
    {
        _results.Remove(purchaseId);
        return Task.CompletedTask;
    }

    // ---- ITemporaryReceiptStore ---------------------------------------------

    Task<TemporaryCapture> ITemporaryReceiptStore.Save(byte[] content, CancellationToken cancellationToken)
    {
        var key = Guid.NewGuid();
        _tempFiles[key] = (content, DateTimeOffset.UtcNow);

        return Task.FromResult(new TemporaryCapture(key, ContentTypeOf(content)));
    }

    public Task<byte[]?> Read(Guid key, CancellationToken cancellationToken = default)
        => Task.FromResult(_tempFiles.TryGetValue(key, out var stored) ? stored.Content : null);

    public Task Delete(Guid key, CancellationToken cancellationToken = default)
    {
        _tempFiles.Remove(key);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> ListOlderThan(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Guid>>([.. _tempFiles
            .Where(entry => entry.Value.WrittenAt < cutoff)
            .Select(entry => entry.Key)]);

    /// <summary>
    /// Stands in for detection from content (7.2), which belongs to the storage adapter. The fake
    /// reads magic bytes rather than a declared type, so nothing above it can come to depend on a
    /// declared type the real adapter ignores.
    /// </summary>
    private static string ContentTypeOf(byte[] content)
        => content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8
            ? "image/jpeg"
            : "application/octet-stream";
}
