using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Application.Services;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain.Entities;

namespace Expenses.Application.Tests.Receipts;

/// <summary>
/// Scenarios from receipt-ingestion, "A fiscal QR payload can be captured with no image": "A payload
/// that yields an invoice", "The service returns no invoice", "A payload no identifier can be read
/// from", "Nothing is held after a fiscal capture"; and from "Fiscal receipt identity is captured when
/// present": "A payload captured on its own".
/// </summary>
public sealed class CaptureFiscalTests
{
    private const string Payload =
        "https://mapr.tax.gov.me/ic/#/verify?iic=32AA324CFF5030271E16D59F7F8EF636"
        + "&tin=02365928&crtd=2026-08-29T14:59:22+02:00&prc=8.48";

    private readonly InMemoryLedger _ledger = new();

    [Fact]
    public async Task A_payload_that_yields_an_invoice()
    {
        var fiscal = FakeStep.Retrieving(
            "fiscal",
            Results.Reconciling("fiscal"),
            new FiscalIdentifiers(Jikr: "a1b2c3d4-0000-0000-0000-000000000000"));
        var vision = FakeStep.Producing("vision", Results.Reconciling("vision")).ReadingImage();

        var result = await Subject(fiscal, vision).CaptureFiscal(Payload);

        Assert.Equal(Purchase.ExtractionState.Extracted, result.State);
        Assert.Equal(2, result.Result?.Candidates.Count);
        Assert.Null(result.TempKey);
        Assert.Equal(0, vision.Runs);

        // The fiscal step works from the payload; there is no image to hand it.
        Assert.Null(fiscal.LastRequest?.Image);
        Assert.Equal(Payload, fiscal.LastRequest?.Payload);
        Assert.Equal(Payload, result.FiscalPayload);
    }

    [Fact]
    public async Task The_service_returns_no_invoice()
    {
        var fiscal = FakeStep.Silent("fiscal");
        var vision = FakeStep.Producing("vision", Results.Reconciling("vision")).ReadingImage();

        var result = await Subject(fiscal, vision).CaptureFiscal(Payload);

        Assert.Equal(Purchase.ExtractionState.Failed, result.State);
        Assert.NotNull(result.FailureReason);
        Assert.Null(result.Result);
        Assert.Equal(0, vision.Runs);

        // What the payload states is still reported, so the caller can decide to send it again with
        // a photograph.
        Assert.Equal("32AA324CFF5030271E16D59F7F8EF636", result.Supplied.Ikof);
        Assert.Equal("02365928", result.Supplied.IssuerTaxNumber);
    }

    [Fact]
    public async Task A_payload_no_identifier_can_be_read_from()
    {
        var result = await Subject(FakeStep.Silent("fiscal")).CaptureFiscal("https://example.com/menu");

        Assert.Equal(Purchase.ExtractionState.Failed, result.State);
        Assert.True(result.Supplied.IsEmpty);
    }

    [Fact]
    public async Task A_payload_captured_on_its_own_is_reported_as_supplied()
    {
        var result = await Subject(FakeStep.Silent("fiscal")).CaptureFiscal(Payload);

        Assert.Equal("32AA324CFF5030271E16D59F7F8EF636", result.Supplied.Ikof);

        // Nothing but the client read it, so nothing was extracted.
        Assert.True(result.Extracted.IsEmpty);
    }

    [Fact]
    public async Task Nothing_is_held_after_a_fiscal_capture()
    {
        await Subject(FakeStep.Retrieving("fiscal", Results.Reconciling("fiscal"), FiscalIdentifiers.None))
            .CaptureFiscal(Payload);

        Assert.Empty(_ledger.Files);
        Assert.False(_ledger.HasAnyTemporaryCapture);
        Assert.Empty(_ledger.Purchases);
        Assert.Equal(0, _ledger.SaveCount);
    }

    private ReceiptService Subject(params IExtractionStep[] steps)
        => new(_ledger, _ledger, _ledger, _ledger, _ledger, _ledger, new ExtractionCascade(steps), _ledger);
}
