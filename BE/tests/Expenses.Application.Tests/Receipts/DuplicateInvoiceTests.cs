using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Application.Services;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain.Entities;

namespace Expenses.Application.Tests.Receipts;

/// <summary>
/// Scenarios from receipt-ingestion, "A capture reports an invoice already recorded": "A receipt
/// scanned twice", "A duplicate can still be confirmed", "A new invoice", "No invoice identification
/// code" — over both capture paths, and whichever way the code was established (D39).
/// </summary>
public sealed class DuplicateInvoiceTests
{
    private const string Ikof = "32AA324CFF5030271E16D59F7F8EF636";

    private const string Payload =
        "https://mapr.tax.gov.me/ic/#/verify?iic=" + Ikof + "&tin=02365928&crtd=2026-08-29T14:59:22+02:00&prc=8.48";

    private static readonly DateTime s_first = new(2026, 8, 29, 14, 59, 22, DateTimeKind.Unspecified);

    private readonly InMemoryLedger _ledger = new();

    public DuplicateInvoiceTests() => _ledger.Given(Unit.Create("PCS", "Piece", "pcs", Unit.UnitKind.Count));

    [Fact]
    public async Task A_receipt_scanned_twice()
    {
        var earlier = GivenRecorded(s_first, supplied: Ikof);

        var result = await Subject(FakeStep.Silent("fiscal")).CaptureFiscal(Payload);

        Assert.Equal(earlier.Id, result.AlreadyRecorded?.PurchaseId);
        Assert.Equal(s_first, result.AlreadyRecorded?.OccurredAt);
    }

    [Fact]
    public async Task The_warning_does_not_change_the_outcome()
    {
        var first = await Subject(FakeStep.Silent("fiscal")).CaptureFiscal(Payload);
        GivenRecorded(s_first, supplied: Ikof);

        var second = await Subject(FakeStep.Silent("fiscal")).CaptureFiscal(Payload);

        Assert.Equal(first.State, second.State);
        Assert.Equal(first.FailureReason, second.FailureReason);
    }

    [Fact]
    public async Task A_code_decoded_from_an_image_is_matched_against_one_recorded_as_extracted()
    {
        var earlier = GivenRecorded(s_first, extracted: Ikof);

        var result = await Subject(
            FakeStep.FiscalOnly("fiscal", new FiscalIdentifiers(Ikof)),
            FakeStep.Producing("vision", Results.Reconciling("vision")))
            .Capture(Jpeg(1));

        Assert.Equal(earlier.Id, result.AlreadyRecorded?.PurchaseId);
    }

    [Fact]
    public async Task A_code_the_service_answered_for_is_matched()
    {
        var earlier = GivenRecorded(s_first, supplied: Ikof);

        var result = await Subject(FakeStep.Retrieving("fiscal", Results.Reconciling("fiscal"), new FiscalIdentifiers(Ikof)))
            .Capture(Jpeg(2));

        Assert.Equal(earlier.Id, result.AlreadyRecorded?.PurchaseId);
    }

    [Fact]
    public async Task The_most_recent_of_several_is_reported()
    {
        GivenRecorded(s_first, supplied: Ikof);
        var later = GivenRecorded(s_first.AddDays(1), supplied: Ikof, amount: 1.00m);

        var result = await Subject(FakeStep.Silent("fiscal")).CaptureFiscal(Payload);

        Assert.Equal(later.Id, result.AlreadyRecorded?.PurchaseId);
    }

    [Fact]
    public async Task A_new_invoice()
    {
        GivenRecorded(s_first, supplied: "SOMEOTHERINVOICE");

        var result = await Subject(FakeStep.Silent("fiscal")).CaptureFiscal(Payload);

        Assert.Null(result.AlreadyRecorded);
    }

    [Fact]
    public async Task No_invoice_identification_code()
    {
        GivenRecorded(s_first, supplied: Ikof);

        var result = await Subject(FakeStep.Producing("vision", Results.Reconciling("vision"))).Capture(Jpeg(3));

        Assert.Null(result.AlreadyRecorded);
    }

    [Fact]
    public async Task A_duplicate_can_still_be_confirmed()
    {
        GivenRecorded(s_first, supplied: Ikof);
        var captured = await Subject(FakeStep.Retrieving("fiscal", Results.Reconciling("fiscal"), FiscalIdentifiers.None))
            .CaptureFiscal(Payload);
        Assert.NotNull(captured.AlreadyRecorded);

        var recorded = await new PurchaseService(_ledger, _ledger, _ledger, new MerchantService(_ledger, _ledger), _ledger, _ledger, _ledger)
            .Record(
                s_first.AddMinutes(1),
                8.48m,
                [new ExpenseCommand("Groceries", 8.48m, UnitCode: "PCS")],
                capture: new CapturedReceiptCommand(
                    TempKey: null,
                    captured.State,
                    FiscalSource: captured.FiscalSource,
                    FiscalPayload: captured.FiscalPayload));

        Assert.False(recorded.AlreadyRecorded);
        Assert.Equal(2, _ledger.Purchases.Count);
    }

    private Purchase GivenRecorded(DateTime occurredAt, string? supplied = null, string? extracted = null, decimal amount = 8.48m)
    {
        var invoice = FiscalInvoice.Create();
        invoice.SupplyIdentifiers(supplied, jikr: null);
        invoice.RecordExtractedIdentifiers(extracted, jikr: null, FiscalInvoice.FiscalSource.DecodedFromCode);

        return _ledger.Given(Purchase.Record(
            occurredAt,
            amount,
            [Expense.Record("Groceries", amount)],
            fiscal: invoice,
            extraction: Purchase.ExtractionState.Extracted));
    }

    private ReceiptService Subject(params IExtractionStep[] steps)
        => new(_ledger, _ledger, _ledger, _ledger, _ledger, _ledger, new ExtractionCascade(steps), _ledger);

    private static byte[] Jpeg(byte seed) => [0xFF, 0xD8, 0xFF, seed, 0x01, 0x02];
}
