using Expenses.Application.Abstractions;
using Expenses.Application.Errors;
using Expenses.Application.Extraction;
using Expenses.Application.Merchants;
using Expenses.Application.Purchases;
using Expenses.Application.Receipts;
using Expenses.Domain;
using Expenses.Infrastructure.Persistence;
using Expenses.Integration.Tests.Harness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Ledger;

/// <summary>
/// The behaviours D17 puts against real PostgreSQL because nothing else can demonstrate them: the
/// unique index under concurrency (D4), ICU collation ordering (D13), trigram tolerance (D14),
/// merchant resolution end to end (D18), rolling a branch up to its chain, and a fiscal
/// disagreement travelling from an upload through extraction (D20).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LedgerBehaviourTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTime Occurred = new(2036, 4, 5, 10, 0, 0, DateTimeKind.Unspecified);

    private static int _sequence;

    private ServiceProvider _services = null!;

    public Task InitializeAsync()
    {
        _services = postgres.Services();
        return postgres.Migrate();
    }

    public Task DisposeAsync() => _services.DisposeAsync().AsTask();

    [Fact]
    public async Task Concurrent_duplicate_submissions()
    {
        var occurred = Next();
        var command = new RecordPurchaseCommand(occurred, 2.90m, [new ExpenseCommand("Coffee", 2.90m)]);

        // Two writers, no coordination: the case a check-then-insert loses and the unique index
        // exists for (D4).
        var results = await Task.WhenAll(
            Task.Run(() => Execute(command)),
            Task.Run(() => Execute(command)));

        Assert.All(results, result => Assert.Equal(results[0].Purchase.Id, result.Purchase.Id));
        Assert.Contains(results, result => result.AlreadyRecorded);

        using var scope = _services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ExpensesDbContext>()
            .Purchases.CountAsync(purchase => purchase.OccurredAt == occurred && purchase.Amount == 2.90m);

        Assert.Equal(1, stored);
    }

    [Fact]
    public async Task Icu_collation_orders_mixed_language_content()
    {
        var codes = new[] { "COLL_ZEBRA", "COLL_ÄPFEL", "COLL_APPLE", "COLL_ČAJ", "COLL_CAKE" };

        using (var scope = _services.CreateScope())
        {
            var categories = scope.ServiceProvider.GetRequiredService<ICategoryRepository>();
            foreach (var code in codes)
            {
                await categories.Add(Category.Create(code, code[5..]));
            }

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges();
        }

        using var reading = _services.CreateScope();
        var context = reading.ServiceProvider.GetRequiredService<ExpensesDbContext>();
        var ordered = await context.Categories
            .Where(category => category.Code.StartsWith("COLL_"))
            .OrderBy(category => category.Name)
            .Select(category => category.Name)
            .ToListAsync();

        // Root ICU collation treats an accented letter as its base letter at the first level, so
        // ÄPFEL and APPLE are separated by their third letter rather than by the diacritic, and ČAJ
        // sorts among the Cs. Byte order would exile every accented word past Z (D13).
        Assert.Equal(["ÄPFEL", "APPLE", "ČAJ", "CAKE", "ZEBRA"], ordered);
    }

    [Fact]
    public async Task Trigram_search_tolerates_accents_and_typos()
    {
        using (var scope = _services.CreateScope())
        {
            var merchants = scope.ServiceProvider.GetRequiredService<IMerchantRepository>();
            await merchants.Add(Merchant.Create("Poslastičarnica Njegoš", "09910001"));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges();
        }

        using var reading = _services.CreateScope();
        var search = reading.ServiceProvider.GetRequiredService<SearchMerchants>();

        // Written without its accents, as a user would type it.
        var unaccented = await search.Execute("Poslasticarnica");
        Assert.Contains(unaccented, match => match.Merchant?.TaxId == "09910001");

        // And with a character-level slip in the middle of the word.
        var mistyped = await search.Execute("Njegos");
        Assert.Contains(mistyped, match => match.Merchant?.TaxId == "09910001");
    }

    [Fact]
    public async Task Merchant_resolution_end_to_end()
    {
        // The same tax number under a different spelling is one merchant.
        var first = await Execute(Purchase(3.00m, new MerchantCommand("AROMA", "09920001")));
        var second = await Execute(Purchase(4.00m, new MerchantCommand("AR0MA d.o.o.", "09920001")));

        Assert.True(first.MerchantNewlyAdded);
        Assert.False(second.MerchantNewlyAdded);
        Assert.Equal(first.Purchase.MerchantId, second.Purchase.MerchantId);
        Assert.Equal("AR0MA d.o.o.", second.Purchase.MerchantRaw);

        // The same name under different tax numbers stays two merchants.
        var third = await Execute(Purchase(5.00m, new MerchantCommand("AROMA", "09920002")));
        Assert.True(third.MerchantNewlyAdded);
        Assert.NotEqual(first.Purchase.MerchantId, third.Purchase.MerchantId);

        // With no tax number, matching falls back to the name.
        var fourth = await Execute(Purchase(6.00m, new MerchantCommand("Pijaca Stall 12")));
        var fifth = await Execute(Purchase(7.00m, new MerchantCommand("Pijaca Stall 12")));

        Assert.True(fourth.MerchantNewlyAdded);
        Assert.False(fifth.MerchantNewlyAdded);
        Assert.Equal(fourth.Purchase.MerchantId, fifth.Purchase.MerchantId);
    }

    [Fact]
    public async Task A_purchase_referencing_a_branch_rolls_up_to_its_parent_chain()
    {
        var chain = await Execute(Purchase(8.00m, new MerchantCommand("VOLI", "09930001")));
        var branch = await Execute(Purchase(9.00m, new MerchantCommand("Voli 034", "09930002")));

        using var scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SetMerchantParent>()
            .Execute(branch.Purchase.MerchantId!.Value, chain.Purchase.MerchantId!.Value);

        var context = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        // What a rollup is, in data terms: the chain plus everything whose parent is the chain.
        var family = await context.Merchants
            .Where(merchant => merchant.Id == chain.Purchase.MerchantId
                || merchant.ParentId == chain.Purchase.MerchantId)
            .Select(merchant => merchant.Id)
            .ToListAsync();

        var total = await context.Purchases
            .Where(purchase => purchase.MerchantId != null && family.Contains(purchase.MerchantId.Value))
            .SumAsync(purchase => purchase.Amount);

        Assert.Equal(17.00m, total);
    }

    [Fact]
    public async Task Fiscal_identifiers_that_disagree_are_both_retained_and_the_image_needs_review()
    {
        // Configured so the decode stage produces an identifier that contradicts the upload.
        await using var services = postgres.Services(("Extraction:Placeholder:Outcome", "Reconciling"));
        using var scope = services.CreateScope();

        var captured = await scope.ServiceProvider.GetRequiredService<CaptureReceipt>().Execute(
            QrReceipt("https://mapr.tax.gov.me/ic/#/verify?iic=DECODED-FROM-IMAGE"),
            new FiscalIdentifiers("SUPPLIED-AT-UPLOAD"));

        var purchase = await scope.ServiceProvider.GetRequiredService<RecordPurchase>().Execute(
            new RecordPurchaseCommand(
                Next(),
                10.00m,
                [new ExpenseCommand("Line", 10.00m)],
                Capture: new CapturedReceiptCommand(
                    captured.TempKey,
                    captured.State,
                    captured.FailureReason,
                    captured.Supplied.Ikof,
                    captured.Supplied.Jikr,
                    captured.Extracted.Ikof,
                    captured.Extracted.Jikr,
                    captured.FiscalSource)));

        var purchaseId = purchase.Purchase.Id;
        var view = await scope.ServiceProvider.GetRequiredService<GetExtractionCandidates>().Execute(purchaseId);

        Assert.Equal("SUPPLIED-AT-UPLOAD", view.Receipt.FiscalIkofSupplied);
        Assert.Equal("DECODED-FROM-IMAGE", view.Receipt.FiscalIkofExtracted);
        Assert.Equal(Receipt.FiscalCorroboration.Disagreed, view.Receipt.Corroboration);
        Assert.Equal(Receipt.ExtractionState.NeedsReview, view.Receipt.State);

        // The numbers were fine; it is the disagreement alone that put it in front of a human. The
        // capture's own validation is what proves that, since a freshly confirmed receipt holds no
        // candidates server-side to recompute it from.
        Assert.True(captured.Validation?.Passed);
    }

    [Fact]
    public async Task A_purchase_with_a_deactivated_category_is_refused_but_history_keeps_it()
    {
        var code = $"RETIRE_{Interlocked.Increment(ref _sequence)}";

        using var scope = _services.CreateScope();
        var categories = scope.ServiceProvider.GetRequiredService<ICategoryRepository>();
        await categories.Add(Category.Create(code, "To be retired"));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges();

        var recorded = await scope.ServiceProvider.GetRequiredService<RecordPurchase>().Execute(
            new RecordPurchaseCommand(Next(), 1.50m, [new ExpenseCommand("Ticket", 1.50m, CategoryCode: code)]));

        await scope.ServiceProvider.GetRequiredService<Application.ReferenceData.DeactivateCategory>()
            .Execute(code);

        var refused = await Assert.ThrowsAsync<ExpensesException>(() =>
            scope.ServiceProvider.GetRequiredService<RecordPurchase>().Execute(
                new RecordPurchaseCommand(Next(), 1.50m, [new ExpenseCommand("Ticket", 1.50m, CategoryCode: code)])));

        Assert.Equal(ApplicationErrors.CategoryInactive, refused.Error.Code);

        // History is untouched: the expense recorded before the category was retired still reports it.
        var stored = await scope.ServiceProvider.GetRequiredService<GetPurchase>().Execute(recorded.Purchase.Id);
        Assert.NotNull(stored.Expenses[0].CategoryId);
    }

    private async Task<RecordPurchaseResult> Execute(RecordPurchaseCommand command)
    {
        using var scope = _services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<RecordPurchase>().Execute(command);
    }

    private static RecordPurchaseCommand Purchase(decimal amount, MerchantCommand? merchant = null) =>
        new(Next(), amount, [new ExpenseCommand("Line", amount)], merchant);

    /// <summary>A PNG carrying a QR code, so the decode stage has something real to read.</summary>
    private static byte[] QrReceipt(string payload) => QrImage.Png(payload);

    private static DateTime Next() => Occurred.AddMinutes(Interlocked.Increment(ref _sequence));
}
