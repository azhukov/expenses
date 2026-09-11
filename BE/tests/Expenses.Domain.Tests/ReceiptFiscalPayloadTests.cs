using Expenses.Domain.Entities;
using ExtractionState = Expenses.Domain.Entities.Receipt.ExtractionState;
using FiscalSource = Expenses.Domain.Entities.Receipt.FiscalSource;

namespace Expenses.Domain.Tests;

/// <summary>
/// Scenarios from receipt-ingestion, "The fiscal QR payload is retained with the receipt":
/// "A supplied payload is retained", "A decoded payload is retained", "An unparseable payload is
/// still retained", "A receipt with no payload".
/// </summary>
public sealed class ReceiptFiscalPayloadTests
{
    /// <summary>The Megapromet receipt's own payload, as its QR carries it.</summary>
    private const string Payload =
        "https://mapr.tax.gov.me/ic/#/verify?iic=32AA324CFF5030271E16D59F7F8EF636"
        + "&tin=02365928&crtd=2026-08-29T14:59:22+02:00&prc=59.65";

    private static Receipt AnImage()
        => Receipt.Of("ab/cd/abcd.jpg", "image/jpeg", sizeInBytes: 2_000_000, ExtractionState.Extracted);

    [Fact]
    public void A_supplied_payload_is_retained()
    {
        var image = AnImage();

        image.RecordFiscalPayload(Payload, FiscalSource.SuppliedAtUpload);

        // Verbatim: the payload is the ground truth a later parser may be asked to re-read (D32).
        Assert.Equal(Payload, image.FiscalPayload);
        Assert.Equal(FiscalSource.SuppliedAtUpload, image.FiscalPayloadSource);
    }

    [Fact]
    public void A_decoded_payload_is_retained()
    {
        var image = AnImage();

        image.RecordFiscalPayload(Payload, FiscalSource.DecodedFromCode);

        Assert.Equal(Payload, image.FiscalPayload);
        Assert.Equal(FiscalSource.DecodedFromCode, image.FiscalPayloadSource);
    }

    [Fact]
    public void An_unparseable_payload_is_still_retained()
    {
        var image = AnImage();

        // Stored precisely because nothing could be read from it: the unparseable case is the one a
        // future parser would want back, and it is unrecoverable once discarded (D32).
        image.RecordFiscalPayload("*not a verification address*", FiscalSource.DecodedFromCode);

        Assert.Equal("*not a verification address*", image.FiscalPayload);
    }

    [Fact]
    public void A_receipt_with_no_payload_records_its_absence()
    {
        var image = AnImage();

        Assert.Null(image.FiscalPayload);
        Assert.Equal(FiscalSource.None, image.FiscalPayloadSource);
    }

    [Fact]
    public void A_payload_that_is_blank_is_no_payload_at_all()
    {
        var image = AnImage();

        image.RecordFiscalPayload("   ", FiscalSource.SuppliedAtUpload);

        Assert.Null(image.FiscalPayload);
        Assert.Equal(FiscalSource.None, image.FiscalPayloadSource);
    }

    [Fact]
    public void A_supplied_payload_is_not_replaced_by_a_decoded_one()
    {
        var image = AnImage();
        image.RecordFiscalPayload(Payload, FiscalSource.SuppliedAtUpload);

        // A supplied payload is preferred outright rather than cross-checked (D31), so a later
        // reading of the same image has nothing to add.
        image.RecordFiscalPayload("*a second reading*", FiscalSource.DecodedFromCode);

        Assert.Equal(Payload, image.FiscalPayload);
        Assert.Equal(FiscalSource.SuppliedAtUpload, image.FiscalPayloadSource);
    }
}
