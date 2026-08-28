using Expenses.Application.Errors;
using Expenses.Application.Extraction;
using Expenses.Application.Receipts;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain;

namespace Expenses.Application.Tests.Receipts;

/// <summary>
/// Scenarios from receipt-ingestion: "A purchase carries at most one receipt image", "The same
/// bytes are not stored twice", "Receipt bytes are stored as files the purchase refers to",
/// "Extraction lifecycle", "Extraction can be re-run", "Fiscal receipt identity is captured when
/// present".
/// </summary>
public sealed class ReceiptTests
{
    private static readonly DateTime Occurred = new(2026, 8, 24, 12, 50, 8, DateTimeKind.Unspecified);

    private readonly InMemoryLedger _ledger = new();

    private AttachReceiptImage Attach => new(_ledger, _ledger, _ledger, _ledger);

    [Fact]
    public async Task Attach_an_image_to_a_purchase()
    {
        var purchase = GivenPurchase();

        var receipt = await Attach.Execute(purchase.Id, Jpeg(1));

        Assert.NotNull(purchase.Receipt);
        Assert.Equal(Receipt.ExtractionState.Pending, receipt.State);
        Assert.Equal("image/jpeg", receipt.ContentType);
    }

    [Fact]
    public async Task Stored_bytes_are_outside_the_ledger()
    {
        var purchase = GivenPurchase();

        await Attach.Execute(purchase.Id, Jpeg(1));

        // The ledger holds the reference to the file, its hash, type and size — never the bytes (D11).
        var receipt = purchase.Receipt!;
        Assert.Equal(Receipt.ContentHashLength, receipt.ContentHash.Length);
        Assert.Equal(6, receipt.SizeInBytes);
        Assert.True(_ledger.Files.ContainsKey(receipt.StorageKey));
    }

    [Fact]
    public async Task State_on_upload()
    {
        var purchase = GivenPurchase();

        var receipt = await Attach.Execute(purchase.Id, Jpeg(1));

        // The upload does not wait for extraction; it queues it, by purchase (D12).
        Assert.Equal(Receipt.ExtractionState.Pending, receipt.State);
        Assert.Equal([purchase.Id], _ledger.Queued);
    }

    [Fact]
    public async Task Attach_a_second_image()
    {
        var purchase = GivenPurchase();
        var first = await Attach.Execute(purchase.Id, Jpeg(1));

        var error = await Assert.ThrowsAsync<ExpensesException>(() => Attach.Execute(purchase.Id, Jpeg(2)));

        Assert.Equal(ApplicationErrors.PurchaseImageAlreadyAttached, error.Error.Code);
        Assert.Equal(first.SizeInBytes, purchase.Receipt!.SizeInBytes);

        // Rejected before anything is written: a refused upload leaves nothing behind.
        Assert.Single(_ledger.Files);
    }

    [Fact]
    public async Task Identical_bytes_uploaded_again()
    {
        var first = GivenPurchase();
        var second = GivenPurchase(amount: 9.99m);

        await Attach.Execute(first.Id, Jpeg(1));
        await Attach.Execute(second.Id, Jpeg(1));

        // One file, two receipts: sharing bytes is a property of the store, not of the ledger (D11).
        Assert.Single(_ledger.Files);
        Assert.Equal(first.Receipt!.StorageKey, second.Receipt!.StorageKey);
        Assert.NotNull(first.Receipt);
        Assert.NotNull(second.Receipt);
    }

    [Fact]
    public async Task Sharing_is_not_visible_in_behaviour()
    {
        var first = GivenPurchase();
        var second = GivenPurchase(amount: 9.99m);
        await Attach.Execute(first.Id, Jpeg(1));
        await Attach.Execute(second.Id, Jpeg(1));

        first.Receipt!.TransitionTo(Receipt.ExtractionState.Extracting);
        first.Receipt!.TransitionTo(Receipt.ExtractionState.Failed, "The engine produced nothing.");

        Assert.Equal(Receipt.ExtractionState.Pending, second.Receipt!.State);
        Assert.Null(second.Receipt!.FailureReason);
    }

    [Fact]
    public async Task Re_photographed_receipt()
    {
        var first = GivenPurchase();
        var second = GivenPurchase(amount: 9.99m);

        await Attach.Execute(first.Id, Jpeg(1));
        await Attach.Execute(second.Id, Jpeg(2));

        Assert.NotEqual(first.Receipt!.StorageKey, second.Receipt!.StorageKey);
        Assert.Equal(2, _ledger.Files.Count);
    }

    [Fact]
    public async Task Identifiers_supplied_with_the_upload()
    {
        var purchase = GivenPurchase();

        var receipt = await Attach.Execute(
            purchase.Id,
            Jpeg(1),
            new FiscalIdentifiers("d1b2c3", "9f8e7d"));

        // Retained without extraction having run.
        Assert.Equal("d1b2c3", receipt.FiscalIkofSupplied);
        Assert.Equal("9f8e7d", receipt.FiscalJikrSupplied);
        Assert.Equal(Receipt.FiscalCorroboration.Unverified, receipt.Corroboration);
    }

    [Fact]
    public async Task Receipt_carries_no_fiscal_identifiers()
    {
        var purchase = GivenPurchase();

        var receipt = await Attach.Execute(purchase.Id, Jpeg(1));

        Assert.Null(receipt.FiscalIkofSupplied);
        Assert.Equal(Receipt.FiscalCorroboration.Absent, receipt.Corroboration);
    }

    [Fact]
    public async Task Upload_for_a_purchase_that_does_not_exist()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() => Attach.Execute(4711, Jpeg(1)));

        Assert.Equal(ApplicationErrors.PurchaseNotFound, error.Error.Code);
        Assert.Empty(_ledger.Files);
    }

    [Fact]
    public async Task Manual_purchase_has_no_image()
    {
        var purchase = GivenPurchase();

        Assert.Null(purchase.Receipt);
    }

    [Fact]
    public async Task Deleting_a_receipt_whose_bytes_another_purchase_shares()
    {
        var first = GivenPurchase();
        var second = GivenPurchase(amount: 9.99m);
        await Attach.Execute(first.Id, Jpeg(1));
        await Attach.Execute(second.Id, Jpeg(1));

        await new DeleteReceipt(_ledger, _ledger, _ledger, _ledger).Execute(first.Id);

        Assert.Null(first.Receipt);
        Assert.NotNull(second.Receipt);
        Assert.True(_ledger.Files.ContainsKey(second.Receipt!.StorageKey));
    }

    [Fact]
    public async Task Deleting_the_last_receipt_referencing_a_file()
    {
        var purchase = GivenPurchase();
        await Attach.Execute(purchase.Id, Jpeg(1));

        await new DeleteReceipt(_ledger, _ledger, _ledger, _ledger).Execute(purchase.Id);

        Assert.Null(purchase.Receipt);
        Assert.Empty(_ledger.Files);
    }

    [Fact]
    public async Task Deleting_a_receipt_leaves_the_expenses_it_produced()
    {
        var purchase = GivenPurchase();
        await Attach.Execute(purchase.Id, Jpeg(1));

        await new DeleteReceipt(_ledger, _ledger, _ledger, _ledger).Execute(purchase.Id);

        Assert.Single(purchase.Expenses);
        Assert.Equal(8.48m, purchase.Amount);
    }

    [Fact]
    public async Task Re_run_a_failed_extraction()
    {
        var purchase = GivenPurchase();
        await Attach.Execute(purchase.Id, Jpeg(1));
        purchase.Receipt!.TransitionTo(Receipt.ExtractionState.Extracting);
        purchase.Receipt!.TransitionTo(Receipt.ExtractionState.Failed, "The engine produced nothing.");
        _ledger.Queued.Clear();

        var requeued = await new RequeueExtraction(_ledger, _ledger, _ledger).Execute(purchase.Id);

        Assert.Equal(Receipt.ExtractionState.Pending, requeued.State);
        Assert.Null(requeued.FailureReason);
        Assert.Equal([purchase.Id], _ledger.Queued);
    }

    [Fact]
    public async Task Re_run_a_completed_extraction()
    {
        var purchase = GivenPurchase();
        await Attach.Execute(purchase.Id, Jpeg(1));
        purchase.Receipt!.TransitionTo(Receipt.ExtractionState.Extracting);
        purchase.Receipt!.TransitionTo(Receipt.ExtractionState.Extracted);
        _ledger.Queued.Clear();

        var requeued = await new RequeueExtraction(_ledger, _ledger, _ledger).Execute(purchase.Id);

        Assert.Equal(Receipt.ExtractionState.Pending, requeued.State);
        Assert.Equal([purchase.Id], _ledger.Queued);
    }

    [Fact]
    public async Task Re_running_a_purchase_without_a_receipt_is_reported()
    {
        var purchase = GivenPurchase();

        var error = await Assert.ThrowsAsync<ExpensesException>(() =>
            new RequeueExtraction(_ledger, _ledger, _ledger).Execute(purchase.Id));

        Assert.Equal(ApplicationErrors.ReceiptImageNotFound, error.Error.Code);
    }

    [Fact]
    public async Task Re_running_a_purchase_that_does_not_exist_is_reported()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() =>
            new RequeueExtraction(_ledger, _ledger, _ledger).Execute(4711));

        Assert.Equal(ApplicationErrors.PurchaseNotFound, error.Error.Code);
    }

    private Purchase GivenPurchase(decimal amount = 8.48m) =>
        _ledger.Given(Purchase.Record(
            Occurred.AddSeconds(amount == 8.48m ? 0 : 1),
            amount,
            [Expense.Record("Groceries", amount)]));

    /// <summary>Bytes that a content sniffer reads as a JPEG, varied by <paramref name="seed"/>.</summary>
    private static byte[] Jpeg(byte seed) => [0xFF, 0xD8, 0xFF, seed, 0x01, 0x02];
}
