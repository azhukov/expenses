using Expenses.Application.Abstractions;
using Expenses.Application.Extraction;
using Expenses.Integration.Tests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Extraction;

/// <summary>
/// What the decoded payload of a real fiscal QR yields, read from the photograph the decoder
/// actually reads rather than from a string restated here.
///
/// Scenarios from receipt-ingestion: "Identity and total are read from the code alone",
/// "An identifier the fiscal code does not carry".
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FiscalPayloadTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Identity_and_total_are_read_from_the_code_alone()
    {
        await using var services = postgres.Services();

        // The decoder is the whole of this: it has no HTTP client and no port to one, so the
        // identity and the total below are established without a network call by construction.
        var decoded = await services.GetRequiredService<IFiscalCodeDecoder>()
            .Decode(DecoderRegressionTests.Photograph(DecoderRegressionTests.DecodableReceipt));

        Assert.Equal(DecoderRegressionTests.Ikof, decoded?.Ikof);
        Assert.Equal("02365928", decoded?.IssuerTaxNumber);
        Assert.Equal("2026-08-29T14:59:22+02:00", decoded?.CreatedAt);
        Assert.Equal(59.65m, decoded?.Total);
    }

    [Fact]
    public async Task An_identifier_the_fiscal_code_does_not_carry()
    {
        await using var services = postgres.Services();

        var decoded = await services.GetRequiredService<IFiscalCodeDecoder>()
            .Decode(DecoderRegressionTests.Photograph(DecoderRegressionTests.DecodableReceipt));

        // The code carries `crtd`, the creation timestamp, and no JIKR at all. Reading the one into
        // the other is the defect this change corrects: every JIKR the shipped code recorded was a
        // timestamp. Until the portal answers, the JIKR is simply not yet known (D24).
        Assert.NotNull(decoded);
        Assert.Null(decoded.Jikr);
        Assert.Equal("2026-08-29T14:59:22+02:00", decoded.CreatedAt);
    }

    [Fact]
    public async Task A_payload_that_is_not_a_url_is_retained_verbatim()
    {
        await using var services = postgres.Services();

        var decoded = await services.GetRequiredService<IFiscalCodeDecoder>()
            .Decode(new ReceiptImageContent(1, "image/png", QrImage.Png("AA-1234/2026 ISSUED AT TILL 3")));

        // No format is imposed on a fiscal identifier (D10): a code this parser does not recognise
        // is kept exactly as it was read rather than discarded or reshaped.
        Assert.Equal("AA-1234/2026 ISSUED AT TILL 3", decoded?.Ikof);
    }
}
