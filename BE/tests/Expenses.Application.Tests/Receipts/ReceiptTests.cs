using Expenses.Application.Abstractions;
using Expenses.Application.Receipts;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain;

namespace Expenses.Application.Tests.Receipts;

/// <summary>
/// Scenarios from receipt-ingestion: "A purchase carries at most one receipt image", "The same
/// bytes are not stored twice", "Receipt bytes are stored as files the purchase refers to".
/// A purchase only ever acquires a receipt at creation now (via <c>RecordPurchase</c>, covered in
/// <c>RecordPurchaseTests</c>), so this file is left with what can still happen to one afterward:
/// deleting it.
/// </summary>
public sealed class ReceiptTests
{
    private static readonly DateTime Occurred = new(2026, 8, 24, 12, 50, 8, DateTimeKind.Unspecified);

    private readonly InMemoryLedger _ledger = new();

    private DeleteReceipt Delete => new(_ledger, _ledger, _ledger, _ledger);

    [Fact]
    public async Task Manual_purchase_has_no_image()
    {
        var purchase = GivenPurchase();

        Assert.Null(purchase.Receipt);
    }

    [Fact]
    public async Task Deleting_a_receipt_whose_bytes_another_purchase_shares()
    {
        var stored = _ledger.GivenReceiptFile(Jpeg(1));
        var first = GivenPurchaseWithReceipt(stored, amount: 8.48m);
        var second = GivenPurchaseWithReceipt(stored, amount: 9.99m);

        await Delete.Execute(first.Id);

        Assert.Null(first.Receipt);
        Assert.NotNull(second.Receipt);
        Assert.True(_ledger.Files.ContainsKey(second.Receipt!.StorageKey));
    }

    [Fact]
    public async Task Deleting_the_last_receipt_referencing_a_file()
    {
        var stored = _ledger.GivenReceiptFile(Jpeg(1));
        var purchase = GivenPurchaseWithReceipt(stored);

        await Delete.Execute(purchase.Id);

        Assert.Null(purchase.Receipt);
        Assert.Empty(_ledger.Files);
    }

    [Fact]
    public async Task Deleting_a_receipt_leaves_the_expenses_it_produced()
    {
        var stored = _ledger.GivenReceiptFile(Jpeg(1));
        var purchase = GivenPurchaseWithReceipt(stored);

        await Delete.Execute(purchase.Id);

        Assert.Single(purchase.Expenses);
        Assert.Equal(8.48m, purchase.Amount);
    }

    private Purchase GivenPurchase(decimal amount = 8.48m)
        => _ledger.Given(Purchase.Record(
            Occurred.AddSeconds(amount == 8.48m ? 0 : 1),
            amount,
            [Expense.Record("Groceries", amount)]));

    private Purchase GivenPurchaseWithReceipt(StoredReceiptFile stored, decimal amount = 8.48m)
        => _ledger.Given(Purchase.Record(
            Occurred.AddSeconds(amount == 8.48m ? 0 : 1),
            amount,
            [Expense.Record("Groceries", amount)],
            receipt: stored.AsReceipt(Receipt.ExtractionState.Extracted)));

    /// <summary>Bytes that a content sniffer reads as a JPEG, varied by <paramref name="seed"/>.</summary>
    private static byte[] Jpeg(byte seed) => [0xFF, 0xD8, 0xFF, seed, 0x01, 0x02];
}
