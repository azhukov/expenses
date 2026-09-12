using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Application.Services;
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
        string? payload = await services.GetRequiredService<IFiscalCodeDecoder>()
            .Decode(DecoderRegressionTests.Photograph(DecoderRegressionTests.DecodableReceipt));
        var identifiers = payload is null ? null : FiscalIdentity.From(payload);

        Assert.Equal(DecoderRegressionTests.Ikof, identifiers?.Ikof);
        Assert.Equal("02365928", identifiers?.IssuerTaxNumber);
        Assert.Equal("2026-08-29T14:59:22+02:00", identifiers?.CreatedAt);
        Assert.Equal(59.65m, identifiers?.Total);
    }

    [Fact]
    public async Task An_identifier_the_fiscal_code_does_not_carry()
    {
        await using var services = postgres.Services();

        string? payload = await services.GetRequiredService<IFiscalCodeDecoder>()
            .Decode(DecoderRegressionTests.Photograph(DecoderRegressionTests.DecodableReceipt));
        var identifiers = payload is null ? null : FiscalIdentity.From(payload);

        // The code carries `crtd`, the creation timestamp, and no JIKR at all. Reading the one into
        // the other is the defect this change corrects: every JIKR the shipped code recorded was a
        // timestamp. Until the portal answers, the JIKR is simply not yet known (D24).
        Assert.NotNull(identifiers);
        Assert.Null(identifiers.Jikr);
        Assert.Equal("2026-08-29T14:59:22+02:00", identifiers.CreatedAt);
    }

    [Fact]
    public async Task A_payload_that_is_not_a_url_is_retained_verbatim()
    {
        await using var services = postgres.Services();

        string? payload = await services.GetRequiredService<IFiscalCodeDecoder>()
            .Decode(new ReceiptImageContent(1, "image/png", QrImage.Png("AA-1234/2026 ISSUED AT TILL 3")));
        var identifiers = payload is null ? null : FiscalIdentity.From(payload);

        // No format is imposed on a fiscal identifier (D10): a code this parser does not recognise
        // is kept exactly as it was read rather than discarded or reshaped.
        Assert.Equal("AA-1234/2026 ISSUED AT TILL 3", identifiers?.Ikof);

        // And the payload itself survives the decode unchanged, which is what makes it worth
        // retaining for a parser that learns more about this format later (D32).
        Assert.Equal("AA-1234/2026 ISSUED AT TILL 3", payload);
    }
}
