using Expenses.Application.Dtos;
using Expenses.Application.Errors;
using Expenses.Application.Services;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain.Entities;

namespace Expenses.Application.Tests.Purchases;

/// <summary>
/// Scenarios from add-clear-all-expenses. purchase-recording: "The number of recorded purchases can
/// be read" ("Counting a populated ledger", "Counting an empty ledger"); "The ledger can be cleared of
/// all purchases" ("Clearing a populated ledger", "Reference data survives a clear", "Clearing an
/// empty ledger"); "A clear must state how many purchases it expects to delete" ("No expected number
/// is given", "The ledger changed since it was counted", "A negative expected number"); "The duplicate
/// guard does not see cleared purchases" ("Re-recording a cleared purchase"). receipt-ingestion:
/// "Receipt files go with the cleared purchases", "Candidates go with the cleared purchases",
/// "Confirming a capture taken before a clear", "Scanning a cleared invoice again".
/// </summary>
public sealed class ClearLedgerTests
{
    private const string Ikof = "32AA324CFF5030271E16D59F7F8EF636";

    private const string Payload =
        "https://mapr.tax.gov.me/ic/#/verify?iic=" + Ikof + "&tin=02365928&crtd=2026-08-29T14:59:22+02:00&prc=8.48";

    private static readonly DateTime s_occurred = new(2026, 8, 19, 10, 15, 0, DateTimeKind.Unspecified);

    private readonly InMemoryLedger _ledger = new();

    public ClearLedgerTests() => _ledger.Given(Unit.Create("PCS", "Piece", "pcs", Unit.UnitKind.Count));

    private PurchaseService Subject
        => new(_ledger, _ledger, _ledger, _ledger, new MerchantService(_ledger, _ledger), _ledger, _ledger, _ledger, _ledger);

    private ReceiptService Receipts
        => new(_ledger, _ledger, _ledger, _ledger, _ledger, _ledger, _ledger, new ExtractionCascade([FakeStep.Silent("fiscal")]), _ledger);

    // ---- Counting -----------------------------------------------------------

    [Fact]
    public async Task Counting_a_populated_ledger()
    {
        for (int month = 1; month <= 12; month++)
        {
            GivenPurchase(new DateTime(2026, month, 3, 9, 0, 0, DateTimeKind.Unspecified), month);
        }

        Assert.Equal(12, await Subject.Count());
    }

    [Fact]
    public async Task Counting_an_empty_ledger() => Assert.Equal(0, await Subject.Count());

    // ---- Clearing -----------------------------------------------------------

    [Fact]
    public async Task Clearing_a_populated_ledger()
    {
        var first = GivenPurchase(s_occurred, 1.00m);
        GivenPurchase(s_occurred.AddMonths(-3), 2.00m);
        GivenPurchase(s_occurred.AddYears(-1), 3.00m);

        var result = await Subject.ClearAll(3);

        Assert.Equal(3, result.Deleted);
        Assert.Empty(await Subject.List());
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.Get(first.Id));
        Assert.Equal(ApplicationErrors.PurchaseNotFound, error.Error.Code);
    }

    [Fact]
    public async Task Reference_data_survives_a_clear()
    {
        _ledger.Given(Category.Create("FOOD", "Food"));
        _ledger.Given(Merchant.Create("AROMA", "02440261"));
        await Subject.Record(s_occurred, 2.50m, [new ExpenseCommand("JAJA", 2.50m, UnitCode: "PCS", UnitRaw: "kom")]);
        Assert.Contains(_ledger.UnitAliases, alias => alias.Alias == "kom");

        await Subject.ClearAll(1);

        Assert.Single(_ledger.Categories);
        Assert.Single(_ledger.Merchants);
        Assert.Contains(_ledger.UnitAliases, alias => alias.Alias == "kom");
        Assert.Equal(0, await Subject.Count());
    }

    [Fact]
    public async Task Clearing_an_empty_ledger()
    {
        var result = await Subject.ClearAll(0);

        Assert.Equal(0, result.Deleted);
        Assert.Empty(result.ReceiptFilesNotRemoved);
    }

    // ---- The guard ----------------------------------------------------------

    [Fact]
    public async Task No_expected_number_is_given()
    {
        GivenPurchase(s_occurred, 1.00m);

        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.ClearAll(null));

        Assert.Equal(ApplicationErrors.PurchaseClearExpectedCountRequired, error.Error.Code);
        Assert.True(error.Error.Fields.ContainsKey("expectedCount"));
        Assert.Equal(1, await Subject.Count());
    }

    [Fact]
    public async Task A_negative_expected_number()
    {
        GivenPurchase(s_occurred, 1.00m);

        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.ClearAll(-1));

        Assert.Equal(ApplicationErrors.PurchaseClearExpectedCountInvalid, error.Error.Code);
        Assert.Equal(-1, error.Error.Fields["expectedCount"]);
        Assert.Equal(1, await Subject.Count());
    }

    [Fact]
    public async Task The_ledger_changed_since_it_was_counted()
    {
        for (int day = 1; day <= 12; day++)
        {
            GivenPurchase(s_occurred.AddDays(-day), day);
        }

        int counted = await Subject.Count();
        GivenPurchase(s_occurred, 13m);

        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.ClearAll(counted));

        Assert.Equal(ApplicationErrors.PurchaseClearCountMismatch, error.Error.Code);
        Assert.Contains("13", error.Error.Message, StringComparison.Ordinal);
        Assert.Equal(13, error.Error.Fields["currentCount"]);
        Assert.Equal(13, await Subject.Count());
    }

    // ---- After a clear ------------------------------------------------------

    [Fact]
    public async Task Re_recording_a_cleared_purchase()
    {
        await Subject.Record(s_occurred, 23.40m, [new ExpenseCommand("Lunch", 23.40m, UnitCode: "PCS")]);
        await Subject.ClearAll(1);

        var again = await Subject.Record(s_occurred, 23.40m, [new ExpenseCommand("Lunch", 23.40m, UnitCode: "PCS")]);

        Assert.False(again.AlreadyRecorded);
        Assert.Equal(1, await Subject.Count());
    }

    [Fact]
    public async Task Candidates_go_with_the_cleared_purchases()
    {
        var purchase = GivenPurchase(s_occurred, 8.48m);
        _ledger.Given(purchase.Id, Results.Reconciling("vision"));

        await Subject.ClearAll(1);

        Assert.Null(await _ledger.FindLatest(purchase.Id));
    }

    [Fact]
    public async Task Receipt_files_go_with_the_cleared_purchases()
    {
        var shared = _ledger.GivenReceiptFile(Jpeg(1));
        var own = _ledger.GivenReceiptFile(Jpeg(2));
        GivenPurchase(s_occurred, 1.00m, shared);
        GivenPurchase(s_occurred.AddDays(1), 2.00m, shared);
        GivenPurchase(s_occurred.AddDays(2), 3.00m, own);

        var result = await Subject.ClearAll(3);

        Assert.Empty(_ledger.Files);
        Assert.Empty(result.ReceiptFilesNotRemoved);
    }

    [Fact]
    public async Task A_file_that_cannot_be_deleted_does_not_fail_the_clear()
    {
        var stuck = _ledger.GivenReceiptFile(Jpeg(1));
        var other = _ledger.GivenReceiptFile(Jpeg(2));
        GivenPurchase(s_occurred, 1.00m, stuck);
        GivenPurchase(s_occurred.AddDays(1), 2.00m, other);
        _ledger.UndeletableFiles.Add(stuck.StorageKey);

        var result = await Subject.ClearAll(2);

        Assert.Equal(2, result.Deleted);
        Assert.Equal([stuck.StorageKey], result.ReceiptFilesNotRemoved);
        Assert.False(_ledger.Files.ContainsKey(other.StorageKey));
        Assert.Equal(0, await Subject.Count());
    }

    [Fact]
    public async Task Confirming_a_capture_taken_before_a_clear()
    {
        GivenPurchase(s_occurred.AddDays(-5), 1.00m);
        var tempKey = _ledger.GivenTemporaryCapture(Jpeg(3));

        await Subject.ClearAll(1);

        Assert.True(_ledger.HasTemporaryCapture(tempKey));
        var recorded = await Subject.Record(
            s_occurred,
            8.48m,
            [new ExpenseCommand("Groceries", 8.48m, UnitCode: "PCS")],
            capture: new CapturedReceiptCommand(tempKey, Purchase.ExtractionState.Extracted));

        Assert.False(recorded.AlreadyRecorded);
        Assert.True(recorded.Purchase.HasReceiptImage);
    }

    [Fact]
    public async Task Scanning_a_cleared_invoice_again()
    {
        var invoice = FiscalInvoice.Create();
        invoice.SupplyIdentifiers(Ikof, jikr: null);
        _ledger.Given(Purchase.Record(
            s_occurred,
            8.48m,
            [Expense.Record("Groceries", 8.48m)],
            fiscal: invoice,
            extraction: Purchase.ExtractionState.Extracted));
        Assert.NotNull((await Receipts.CaptureFiscal(Payload)).AlreadyRecorded);

        await Subject.ClearAll(1);

        Assert.Null((await Receipts.CaptureFiscal(Payload)).AlreadyRecorded);
    }

    private Purchase GivenPurchase(DateTime occurredAt, decimal amount, StoredReceiptFile? receipt = null)
        => _ledger.Given(Purchase.Record(
            occurredAt,
            amount,
            [Expense.Record("Groceries", amount)],
            receipt: receipt?.AsReceipt(),
            extraction: receipt is null ? null : Purchase.ExtractionState.Extracted));

    private static byte[] Jpeg(byte seed) => [0xFF, 0xD8, 0xFF, seed, 0x01, 0x02];
}
