using Expenses.Domain.Entities;
using FiscalSource = Expenses.Domain.Entities.FiscalInvoice.FiscalSource;

namespace Expenses.Domain.Tests;

/// <summary>
/// Scenarios from receipt-ingestion, "The fiscal QR payload is retained with the receipt":
/// "A supplied payload is retained", "A decoded payload is retained", "An unparseable payload is
/// still retained", "A receipt with no payload".
/// </summary>
public sealed class FiscalInvoiceTests
{
    /// <summary>The Megapromet receipt's own payload, as its QR carries it.</summary>
    private const string Payload =
        "https://mapr.tax.gov.me/ic/#/verify?iic=32AA324CFF5030271E16D59F7F8EF636"
        + "&tin=02365928&crtd=2026-08-29T14:59:22+02:00&prc=59.65";

    private static FiscalInvoice AnInvoice()
        => FiscalInvoice.Create();

    [Fact]
    public void A_supplied_payload_is_retained()
    {
        var invoice = AnInvoice();

        invoice.RecordPayload(Payload, FiscalSource.SuppliedAtUpload);

        // Verbatim: the payload is the ground truth a later parser may be asked to re-read (D32).
        Assert.Equal(Payload, invoice.FiscalPayload);
        Assert.Equal(FiscalSource.SuppliedAtUpload, invoice.FiscalPayloadSource);
    }

    [Fact]
    public void A_decoded_payload_is_retained()
    {
        var invoice = AnInvoice();

        invoice.RecordPayload(Payload, FiscalSource.DecodedFromCode);

        Assert.Equal(Payload, invoice.FiscalPayload);
        Assert.Equal(FiscalSource.DecodedFromCode, invoice.FiscalPayloadSource);
    }

    [Fact]
    public void An_unparseable_payload_is_still_retained()
    {
        var invoice = AnInvoice();

        // Stored precisely because nothing could be read from it: the unparseable case is the one a
        // future parser would want back, and it is unrecoverable once discarded (D32).
        invoice.RecordPayload("*not a verification address*", FiscalSource.DecodedFromCode);

        Assert.Equal("*not a verification address*", invoice.FiscalPayload);
    }

    [Fact]
    public void An_invoice_with_no_payload_records_its_absence()
    {
        var invoice = AnInvoice();

        Assert.Null(invoice.FiscalPayload);
        Assert.Equal(FiscalSource.None, invoice.FiscalPayloadSource);
    }

    [Fact]
    public void A_payload_that_is_blank_is_no_payload_at_all()
    {
        var invoice = AnInvoice();

        invoice.RecordPayload("   ", FiscalSource.SuppliedAtUpload);

        Assert.Null(invoice.FiscalPayload);
        Assert.Equal(FiscalSource.None, invoice.FiscalPayloadSource);
    }

    [Fact]
    public void A_supplied_payload_is_not_replaced_by_a_decoded_one()
    {
        var invoice = AnInvoice();
        invoice.RecordPayload(Payload, FiscalSource.SuppliedAtUpload);

        // A supplied payload is preferred outright rather than cross-checked (D31), so a later
        // reading of the same image has nothing to add.
        invoice.RecordPayload("*a second reading*", FiscalSource.DecodedFromCode);

        Assert.Equal(Payload, invoice.FiscalPayload);
        Assert.Equal(FiscalSource.SuppliedAtUpload, invoice.FiscalPayloadSource);
    }
}
