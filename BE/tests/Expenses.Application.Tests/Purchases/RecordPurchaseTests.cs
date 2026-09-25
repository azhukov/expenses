using Expenses.Application.Dtos;
using Expenses.Application.Errors;
using Expenses.Application.Services;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain.Entities;

namespace Expenses.Application.Tests.Purchases;

/// <summary>
/// Scenarios from purchase-recording: "Purchase is a container of expenses", "Purchase amount
/// reconciles with its expenses", "Duplicate purchase submissions are absorbed", "A purchase
/// records where it was made"; and from reference-data: "Entries are retired, not deleted",
/// "Merchants are a dictionary learned during ingestion".
/// </summary>
public sealed class RecordPurchaseTests
{
    private static readonly DateTime s_occurred = new(2026, 8, 19, 14, 3, 0, DateTimeKind.Unspecified);

    /// <summary>
    /// The unit a test supplies when the unit is not what it is about. Every line needs one
    /// ("An expense without a unit is rejected"), so the tests that are about something else say
    /// so with the plainest unit there is rather than leaving it out.
    /// </summary>
    private const string Piece = "PCS";

    private readonly InMemoryLedger _ledger = new();

    public RecordPurchaseTests() => _ledger.Given(Unit.Create(Piece, "Piece", "pcs", Unit.UnitKind.Count));

    private PurchaseService Subject
        => new(_ledger, _ledger, _ledger, new MerchantService(_ledger, _ledger), _ledger, _ledger, _ledger);

    [Fact]
    public async Task Manual_single_line_entry()
    {
        var result = await Subject.Record(
            new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Unspecified),
            12.40m,
            [new ExpenseCommand("Lunch", 12.40m, UnitCode: Piece)]);

        Assert.False(result.AlreadyRecorded);
        Assert.Equal(12.40m, result.Purchase.Amount);
        Assert.Equal(new DateTime(2026, 8, 19, 0, 0, 0), result.Purchase.OccurredAt);
        var expense = Assert.Single(result.Purchase.Expenses);
        Assert.Equal("Lunch", expense.Description);
        Assert.Single(_ledger.Purchases);
    }

    [Fact]
    public async Task Purchase_with_no_expenses_is_rejected()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() =>
            Subject.Record(s_occurred, 80.00m, []));

        Assert.Equal(ApplicationErrors.PurchaseNoExpenses, error.Error.Code);
        Assert.Empty(_ledger.Purchases);
    }

    [Fact]
    public async Task Amounts_do_not_reconcile()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.Record(
            s_occurred,
            80.00m,
            [new ExpenseCommand("Shoes", 60.00m, UnitCode: Piece), new ExpenseCommand("Socks", 18.50m, UnitCode: Piece)]));

        Assert.Equal(ApplicationErrors.PurchaseReconciliationMismatch, error.Error.Code);
        Assert.Equal(80.00m, error.Error.Fields["amount"]);
        Assert.Equal(78.50m, error.Error.Fields["expensesTotal"]);
        Assert.Empty(_ledger.Purchases);
    }

    [Fact]
    public async Task Reconciliation_is_exact_not_approximate()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.Record(
            s_occurred,
            10.00m,
            [
                new ExpenseCommand("Third", 3.33m, UnitCode: Piece),
                new ExpenseCommand("Third", 3.33m, UnitCode: Piece),
                new ExpenseCommand("Third", 3.33m, UnitCode: Piece),
            ]));

        Assert.Equal(ApplicationErrors.PurchaseReconciliationMismatch, error.Error.Code);
        Assert.Equal(9.99m, error.Error.Fields["expensesTotal"]);
    }

    [Fact]
    public async Task Amounts_cannot_be_negative()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.Record(
            s_occurred,
            -12.40m,
            [new ExpenseCommand("Refund", -12.40m, UnitCode: Piece)]));

        Assert.Equal(ApplicationErrors.AmountNegative, error.Error.Code);
    }

    [Fact]
    public async Task Same_purchase_submitted_twice()
    {
        IReadOnlyList<ExpenseCommand> expenses = [new ExpenseCommand("Lunch", 12.40m, UnitCode: Piece)];

        var first = await Subject.Record(s_occurred, 12.40m, expenses);
        var second = await Subject.Record(s_occurred, 12.40m, expenses);

        Assert.False(first.AlreadyRecorded);
        Assert.True(second.AlreadyRecorded);
        Assert.Equal(first.Purchase.Id, second.Purchase.Id);
        Assert.Single(_ledger.Purchases);
    }

    [Fact]
    public async Task Concurrent_duplicate_submissions()
    {
        // The guard queried, found nothing, and another writer committed first — the case the
        // unique index exists for (D4). The insert must lose and the winner must be returned.
        _ledger.ConcurrentWinner = Purchase.Record(s_occurred, 12.40m, [Expense.Record("Lunch", 12.40m)]);

        var result = await Subject.Record(
            s_occurred,
            12.40m,
            [new ExpenseCommand("Lunch", 12.40m, UnitCode: Piece)]);

        Assert.True(result.AlreadyRecorded);
        Assert.Single(_ledger.Purchases);
        Assert.Equal(_ledger.Purchases[0].Id, result.Purchase.Id);
    }

    [Fact]
    public async Task Expenses_of_a_duplicate_submission_are_ignored()
    {
        await Subject.Record(s_occurred, 12.40m, [new ExpenseCommand("Lunch", 12.40m, UnitCode: Piece)]);

        var second = await Subject.Record(
            s_occurred,
            12.40m,
            [new ExpenseCommand("Soup", 5.40m, UnitCode: Piece), new ExpenseCommand("Bread", 7.00m, UnitCode: Piece)]);

        Assert.True(second.AlreadyRecorded);
        var expense = Assert.Single(second.Purchase.Expenses);
        Assert.Equal("Lunch", expense.Description);
    }

    [Fact]
    public async Task Differing_amount_is_not_a_duplicate()
    {
        await Subject.Record(s_occurred, 12.40m, [new ExpenseCommand("Lunch", 12.40m, UnitCode: Piece)]);
        var second = await Subject.Record(
            s_occurred,
            12.50m,
            [new ExpenseCommand("Lunch", 12.50m, UnitCode: Piece)]);

        Assert.False(second.AlreadyRecorded);
        Assert.Equal(2, _ledger.Purchases.Count);
    }

    [Fact]
    public async Task Differing_occurrence_is_not_a_duplicate()
    {
        await Subject.Record(s_occurred, 12.40m, [new ExpenseCommand("Lunch", 12.40m, UnitCode: Piece)]);
        var second = await Subject.Record(
            new DateTime(2026, 8, 19, 15, 20, 0, DateTimeKind.Unspecified),
            12.40m,
            [new ExpenseCommand("Lunch", 12.40m, UnitCode: Piece)]);

        Assert.False(second.AlreadyRecorded);
        Assert.Equal(2, _ledger.Purchases.Count);
    }

    [Fact]
    public async Task Two_identical_same_day_purchases_with_unknown_times()
    {
        // Specified behaviour, not a defect (D3): the guard collides here, and the escape hatches
        // are supplying a time or recording the second as a further expense.
        var midnight = new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Unspecified);
        var first = await Subject.Record(
            midnight,
            2.90m,
            [new ExpenseCommand("Coffee", 2.90m, UnitCode: Piece)]);

        var second = await Subject.Record(
            midnight,
            2.90m,
            [new ExpenseCommand("Coffee", 2.90m, UnitCode: Piece)]);

        Assert.True(second.AlreadyRecorded);
        Assert.Equal(first.Purchase.Id, second.Purchase.Id);
        Assert.Single(_ledger.Purchases);

        // The first escape hatch: a time of day makes it a different purchase.
        var withTime = await Subject.Record(
            new DateTime(2026, 8, 19, 16, 30, 0, DateTimeKind.Unspecified),
            2.90m,
            [new ExpenseCommand("Coffee", 2.90m, UnitCode: Piece)]);

        Assert.False(withTime.AlreadyRecorded);
    }

    [Fact]
    public async Task Merchant_does_not_affect_the_duplicate_guard()
    {
        var occurred = new DateTime(2026, 8, 24, 12, 50, 8, DateTimeKind.Unspecified);
        var first = await Subject.Record(
            occurred,
            8.48m,
            [new ExpenseCommand("Groceries", 8.48m, UnitCode: Piece)],
            new MerchantCommand("AROMA", "02440261"));

        var second = await Subject.Record(
            occurred,
            8.48m,
            [new ExpenseCommand("Groceries", 8.48m, UnitCode: Piece)],
            new MerchantCommand("VOLI", "03001234"));

        Assert.True(second.AlreadyRecorded);
        Assert.Equal(first.Purchase.MerchantId, second.Purchase.MerchantId);
        Assert.Equal("AROMA", second.Purchase.MerchantRaw);
        Assert.Single(_ledger.Purchases);
        Assert.Single(_ledger.Merchants);
    }

    [Fact]
    public async Task First_purchase_from_a_new_merchant()
    {
        var result = await Subject.Record(
            s_occurred,
            8.48m,
            [new ExpenseCommand("Groceries", 8.48m, UnitCode: Piece)],
            new MerchantCommand("AROMA", "02440261"));

        Assert.True(result.MerchantNewlyAdded);
        var merchant = Assert.Single(_ledger.Merchants);
        Assert.Equal("AROMA", merchant.Name);
        Assert.Equal(merchant.Id, result.Purchase.MerchantId);
        Assert.Equal("AROMA", result.Purchase.MerchantRaw);
    }

    [Fact]
    public async Task Second_purchase_from_a_known_merchant()
    {
        await Subject.Record(
            s_occurred,
            8.48m,
            [new ExpenseCommand("Groceries", 8.48m, UnitCode: Piece)],
            new MerchantCommand("AROMA", "02440261"));

        var second = await Subject.Record(
            s_occurred.AddDays(1),
            3.20m,
            [new ExpenseCommand("Coffee", 3.20m, UnitCode: Piece)],
            new MerchantCommand("AROMA", "02440261"));

        Assert.False(second.MerchantNewlyAdded);
        Assert.Single(_ledger.Merchants);
        Assert.Equal(_ledger.Merchants[0].Id, second.Purchase.MerchantId);
    }

    [Fact]
    public async Task Same_tax_number_under_a_different_spelling_is_the_same_merchant()
    {
        await Subject.Record(
            s_occurred,
            8.48m,
            [new ExpenseCommand("Groceries", 8.48m, UnitCode: Piece)],
            new MerchantCommand("AROMA", "02440261"));

        var second = await Subject.Record(
            s_occurred.AddDays(1),
            3.20m,
            [new ExpenseCommand("Coffee", 3.20m, UnitCode: Piece)],
            new MerchantCommand("AR0MA d.o.o.", "02440261"));

        Assert.Single(_ledger.Merchants);
        Assert.False(second.MerchantNewlyAdded);

        // The verbatim text is the receipt's, never the dictionary's (D9).
        Assert.Equal("AR0MA d.o.o.", second.Purchase.MerchantRaw);
    }

    [Fact]
    public async Task Matching_later_does_not_erase_merchant_text()
    {
        var result = await Subject.Record(
            s_occurred,
            8.48m,
            [new ExpenseCommand("Groceries", 8.48m, UnitCode: Piece)],
            new MerchantCommand("Aroma 034"));

        Assert.Equal("Aroma 034", result.Purchase.MerchantRaw);
        Assert.NotNull(result.Purchase.MerchantId);
    }

    [Fact]
    public async Task Purchase_with_no_merchant()
    {
        var result = await Subject.Record(
            s_occurred,
            8.48m,
            [new ExpenseCommand("Groceries", 8.48m, UnitCode: Piece)]);

        Assert.Null(result.Purchase.MerchantId);
        Assert.Null(result.Purchase.MerchantRaw);
        Assert.Empty(_ledger.Merchants);
    }

    [Fact]
    public async Task Assigning_a_deactivated_category_is_rejected()
    {
        var commuting = _ledger.Given(Category.Create("COMMUTING", "Commuting"));
        commuting.Deactivate();

        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.Record(
            s_occurred,
            2.00m,
            [new ExpenseCommand("Bus fare", 2.00m, CategoryCode: "COMMUTING")]));

        Assert.Equal(ApplicationErrors.CategoryInactive, error.Error.Code);
        Assert.Empty(_ledger.Purchases);
    }

    [Fact]
    public async Task Assigning_a_deactivated_unit_is_rejected()
    {
        var bunch = _ledger.Given(Unit.Create("BUNCH", "Bunch", "bund", Unit.UnitKind.Count));
        bunch.Deactivate();

        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.Record(
            s_occurred,
            2.00m,
            [new ExpenseCommand("Radishes", 2.00m, UnitCode: "BUNCH")]));

        Assert.Equal(ApplicationErrors.UnitInactive, error.Error.Code);
    }

    [Fact]
    public async Task An_over_long_description_is_rejected()
    {
        string description = new('x', 201);

        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.Record(
            s_occurred,
            2.00m,
            [new ExpenseCommand(description, 2.00m, UnitCode: Piece)]));

        Assert.Equal(ApplicationErrors.ExpenseDescriptionTooLong, error.Error.Code);
        Assert.Contains("200", error.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_ledger.Purchases);
    }

    [Fact]
    public async Task An_expense_without_a_unit_is_rejected()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.Record(
            s_occurred,
            2.00m,
            [new ExpenseCommand("Bus fare", 2.00m)]));

        Assert.Equal(ApplicationErrors.ExpenseUnitRequired, error.Error.Code);
        Assert.Empty(_ledger.Purchases);
    }

    /// <summary>
    /// Which line is at fault is what the user needs in order to correct it, and a code on its own
    /// does not say — so the offending position travels in the error's fields.
    /// </summary>
    [Fact]
    public async Task A_rejected_line_without_a_unit_is_named()
    {
        var kilogram = _ledger.Given(Unit.Create("KG", "Kilogram", "kg", Unit.UnitKind.Mass));

        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.Record(
            s_occurred,
            4.00m,
            [
                new ExpenseCommand("Bananas", 2.00m, UnitCode: "KG"),
                new ExpenseCommand("Bus fare", 2.00m),
            ]));

        Assert.Equal(ApplicationErrors.ExpenseUnitRequired, error.Error.Code);
        Assert.Equal(2, Assert.Contains("line", error.Error.Fields));
        Assert.NotEqual(0, kilogram.Id);
    }

    [Fact]
    public async Task A_unit_matched_during_extraction_satisfies_the_rule()
    {
        var kilogram = _ledger.Given(Unit.Create("KG", "Kilogram", "kg", Unit.UnitKind.Mass));

        var result = await Subject.Record(
            s_occurred,
            1.06m,
            [new ExpenseCommand("Bananas", 1.06m) { MatchedUnitId = kilogram.Id }]);

        var expense = Assert.Single(result.Purchase.Expenses);
        Assert.Equal(kilogram.Id, expense.UnitId);
    }

    [Fact]
    public async Task Unknown_category_code_is_rejected()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.Record(
            s_occurred,
            2.00m,
            [new ExpenseCommand("Bus fare", 2.00m, UnitCode: Piece, CategoryCode: "NOPE")]));

        Assert.Equal(ApplicationErrors.CategoryNotFound, error.Error.Code);
    }

    [Fact]
    public async Task Matched_reference_and_verbatim_text_coexist()
    {
        var kilogram = _ledger.Given(Unit.Create("KG", "Kilogram", "kg", Unit.UnitKind.Mass));

        var result = await Subject.Record(
            s_occurred,
            1.06m,
            [new ExpenseCommand(
                "Bananas",
                1.06m,
                Quantity: 0.482m,
                UnitCode: "KG",
                UnitPrice: 2.19m,
                UnitRaw: "kg.")]);

        var expense = Assert.Single(result.Purchase.Expenses);
        Assert.Equal(kilogram.Id, expense.UnitId);
        Assert.Equal("kg.", expense.UnitRaw);
        Assert.Equal(0.482m, expense.Quantity);
        Assert.Equal(2.19m, expense.UnitPrice);
    }

    [Fact]
    public async Task Savings_are_reported_for_a_purchase()
    {
        var result = await Subject.Record(
            s_occurred,
            8.48m,
            [
                new ExpenseCommand("Sladoled", 4.49m, UnitCode: Piece, ListUnitPrice: 8.50m, DiscountAmount: 4.01m),
                new ExpenseCommand("Cokolada", 3.99m, UnitCode: Piece, ListUnitPrice: 7.50m, DiscountAmount: 3.51m),
            ]);

        Assert.Equal(7.52m, result.Purchase.TotalSaving);
        Assert.Equal(4.49m, result.Purchase.Expenses[0].Amount);
    }

    [Fact]
    public async Task Negative_discount_is_rejected()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.Record(
            s_occurred,
            4.49m,
            [new ExpenseCommand("Sladoled", 4.49m, UnitCode: Piece, ListUnitPrice: 8.50m, DiscountAmount: -4.01m)]));

        Assert.Equal(ApplicationErrors.ExpenseDiscountNegative, error.Error.Code);
    }

    [Fact]
    public async Task List_price_and_discount_are_recorded_together_or_not_at_all()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.Record(
            s_occurred,
            4.49m,
            [new ExpenseCommand("Sladoled", 4.49m, UnitCode: Piece, ListUnitPrice: 8.50m)]));

        Assert.Equal(ApplicationErrors.ExpenseDiscountIncomplete, error.Error.Code);
    }

    // ---- Confirming a capture (receipt-ingestion) --------------------------

    [Fact]
    public async Task A_confirmed_capture_becomes_a_whole_purchase()
    {
        var tempKey = _ledger.GivenTemporaryCapture(Jpeg(1));

        var result = await Subject.Record(
            s_occurred,
            8.48m,
            [new ExpenseCommand("Groceries", 8.48m, UnitCode: Piece)],
            capture: new CapturedReceiptCommand(tempKey, Purchase.ExtractionState.Extracted));

        Assert.False(result.AlreadyRecorded);
        Assert.True(result.Purchase.HasReceiptImage);
        Assert.Equal(Purchase.ExtractionState.Extracted, _ledger.Purchases[0].Extraction);
    }

    [Fact]
    public async Task Confirming_promotes_the_temporary_image()
    {
        var tempKey = _ledger.GivenTemporaryCapture(Jpeg(2));

        await Subject.Record(
            s_occurred,
            8.48m,
            [new ExpenseCommand("Groceries", 8.48m, UnitCode: Piece)],
            capture: new CapturedReceiptCommand(tempKey, Purchase.ExtractionState.Extracted));

        Assert.False(_ledger.HasTemporaryCapture(tempKey));
        Assert.Single(_ledger.Files);
    }

    [Fact]
    public async Task Confirmation_that_does_not_reconcile()
    {
        var tempKey = _ledger.GivenTemporaryCapture(Jpeg(3));

        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.Record(
            s_occurred,
            8.48m,
            [new ExpenseCommand("Groceries", 5.00m, UnitCode: Piece)],
            capture: new CapturedReceiptCommand(tempKey, Purchase.ExtractionState.Extracted)));

        Assert.Equal(ApplicationErrors.PurchaseReconciliationMismatch, error.Error.Code);
        Assert.Empty(_ledger.Purchases);

        // The temporary capture is left in place, available to confirm again.
        Assert.True(_ledger.HasTemporaryCapture(tempKey));
        Assert.Empty(_ledger.Files);
    }

    [Fact]
    public async Task Confirming_an_unknown_or_expired_key()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.Record(
            s_occurred,
            8.48m,
            [new ExpenseCommand("Groceries", 8.48m, UnitCode: Piece)],
            capture: new CapturedReceiptCommand(Guid.NewGuid(), Purchase.ExtractionState.Extracted)));

        Assert.Equal(ApplicationErrors.CaptureNotFound, error.Error.Code);
        Assert.Empty(_ledger.Purchases);
    }

    [Fact]
    public async Task Date_taken_from_a_decoded_fiscal_receipt()
    {
        var tempKey = _ledger.GivenTemporaryCapture(Jpeg(4));

        var result = await Subject.Record(
            occurredAt: null,
            8.48m,
            [new ExpenseCommand("Groceries", 8.48m, UnitCode: Piece)],
            capture: new CapturedReceiptCommand(
                tempKey,
                Purchase.ExtractionState.Extracted,
                FiscalPayload: "https://mapr.tax.gov.me/ic/#/verify?iic=A1B2C3&crtd=2026-08-24T12:50:08+02:00"));

        Assert.Equal(new DateTime(2026, 8, 24, 12, 50, 8), result.Purchase.OccurredAt);
    }

    [Fact]
    public async Task Callers_date_overrides_the_receipt()
    {
        var tempKey = _ledger.GivenTemporaryCapture(Jpeg(5));

        var result = await Subject.Record(
            new DateTime(2026, 8, 25, 9, 0, 0, DateTimeKind.Unspecified),
            8.48m,
            [new ExpenseCommand("Groceries", 8.48m, UnitCode: Piece)],
            capture: new CapturedReceiptCommand(
                tempKey,
                Purchase.ExtractionState.Extracted,
                FiscalPayload: "https://mapr.tax.gov.me/ic/#/verify?iic=A1B2C3&crtd=2026-08-24T12:50:08+02:00"));

        Assert.Equal(new DateTime(2026, 8, 25, 9, 0, 0), result.Purchase.OccurredAt);
    }

    [Fact]
    public async Task No_date_anywhere()
    {
        var tempKey = _ledger.GivenTemporaryCapture(Jpeg(6));

        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.Record(
            occurredAt: null,
            8.48m,
            [new ExpenseCommand("Groceries", 8.48m, UnitCode: Piece)],
            capture: new CapturedReceiptCommand(tempKey, Purchase.ExtractionState.Extracted)));

        Assert.Equal(ApplicationErrors.PurchaseOccurrenceRequired, error.Error.Code);
        Assert.Empty(_ledger.Purchases);
    }

    /// <summary>receipt-ingestion, "A fiscal-only capture is confirmed by its payload".</summary>
    [Fact]
    public async Task A_confirmed_fiscal_capture_becomes_a_whole_purchase()
    {
        var result = await Subject.Record(
            s_occurred,
            8.48m,
            [new ExpenseCommand("Groceries", 8.48m, UnitCode: Piece)],
            capture: new CapturedReceiptCommand(
                TempKey: null,
                Purchase.ExtractionState.Extracted,
                Jikr: "a1b2c3d4-0000-0000-0000-000000000000",
                FiscalSource: FiscalInvoice.FiscalSource.RetrievedFromService,
                FiscalPayload: FiscalPayload));

        var purchase = Assert.Single(_ledger.Purchases);
        Assert.False(result.AlreadyRecorded);
        Assert.Null(purchase.Receipt);
        Assert.Equal(Purchase.ExtractionState.Extracted, purchase.Extraction);

        // Parsed again from the payload the caller resubmitted, not taken from anything it echoed
        // (D30, D38); the identifier only the service knows arrives as submitted (D24).
        Assert.NotNull(purchase.Fiscal);
        Assert.Equal(FiscalPayload, purchase.Fiscal.FiscalPayload);
        Assert.Equal("32AA324CFF5030271E16D59F7F8EF636", purchase.Fiscal.FiscalIkofExtracted);
        Assert.Equal("a1b2c3d4-0000-0000-0000-000000000000", purchase.Fiscal.FiscalJikrExtracted);
        Assert.Equal(FiscalInvoice.FiscalSource.RetrievedFromService, purchase.Fiscal.FiscalExtractedSource);

        // Nothing was stored, because there were no bytes to store.
        Assert.Empty(_ledger.Files);
    }

    [Fact]
    public async Task The_date_defaults_from_the_payload()
    {
        var result = await Subject.Record(
            occurredAt: null,
            8.48m,
            [new ExpenseCommand("Groceries", 8.48m, UnitCode: Piece)],
            capture: new CapturedReceiptCommand(
                TempKey: null,
                Purchase.ExtractionState.Extracted,
                FiscalSource: FiscalInvoice.FiscalSource.RetrievedFromService,
                FiscalPayload: FiscalPayload));

        Assert.Equal(new DateTime(2026, 8, 29, 14, 59, 22), result.Purchase.OccurredAt);
    }

    [Fact]
    public async Task A_confirmation_naming_neither_an_image_nor_a_payload()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Subject.Record(
            s_occurred,
            8.48m,
            [new ExpenseCommand("Groceries", 8.48m, UnitCode: Piece)],
            capture: new CapturedReceiptCommand(TempKey: null, Purchase.ExtractionState.Extracted)));

        Assert.Equal(ApplicationErrors.CaptureIdentityRequired, error.Error.Code);
        Assert.Empty(_ledger.Purchases);
    }

    private const string FiscalPayload =
        "https://mapr.tax.gov.me/ic/#/verify?iic=32AA324CFF5030271E16D59F7F8EF636"
        + "&tin=02365928&crtd=2026-08-29T14:59:22+02:00&prc=8.48";

    private static byte[] Jpeg(byte seed) => [0xFF, 0xD8, 0xFF, seed, 0x01, 0x02];
}
