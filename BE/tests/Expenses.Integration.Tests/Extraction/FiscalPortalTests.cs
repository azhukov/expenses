using System.Net;
using Expenses.Application.Extraction;
using Expenses.Integration.Tests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Extraction;

/// <summary>
/// Retrieval from the fiscal verification service, against the response recorded once from the real
/// portal and committed with the photographs (D26). Nothing here reaches the network.
///
/// Scenarios from receipt-ingestion: "Invoice detail is retrieved and mapped", "The missing
/// identifier arrives from the verification service", "The service has no record of the invoice",
/// "The service cannot be reached", "Retrieval is not repeated needlessly".
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FiscalPortalTests(PostgresFixture postgres)
{
    public const string Stage = "fiscal-portal";

    /// <summary>What the Megapromet receipt's own QR carries, and all the portal is given.</summary>
    private static readonly FiscalIdentifiers Decoded = new(
        DecoderRegressionTests.Ikof,
        Jikr: null,
        IssuerTaxNumber: "02365928",
        CreatedAt: "2026-08-29T14:59:22+02:00",
        Total: 59.65m);

    [Fact]
    public async Task Invoice_detail_is_retrieved_and_mapped()
    {
        await using var portal = await FiscalPortalStub.Answering();
        await using var services = Services(portal);

        var invoice = await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, Decoded);

        Assert.NotNull(invoice);
        Assert.Equal(7, invoice.Result.PurchaseId);
        Assert.Equal(14, invoice.Result.Candidates.Count);
        Assert.Equal(59.65m, invoice.Result.Total);

        // Verbatim, all of it: the tax authority has already stated these and nothing here is
        // entitled to a second opinion (D27).
        var wine = invoice.Result.Candidates[0];
        Assert.Equal("CABERNET SYRAH 0.75L", wine.Description);
        Assert.Equal(1m, wine.Quantity);
        Assert.Equal("KOM", wine.UnitRaw);
        Assert.Equal(9.75m, wine.Amount);
        Assert.Equal(Stage, wine.Provenance[ExtractedValues.Amount]);

        // A line priced by weight: the quantity is not a whole number and the amount is not the
        // unit price, which is exactly the case a mapping is most likely to get wrong.
        var poultry = invoice.Result.Candidates[3];
        Assert.Equal("CURECI FILE svjezi", poultry.Description);
        Assert.Equal(0.548m, poultry.Quantity);
        Assert.Equal("KG", poultry.UnitRaw);
        Assert.Equal(8.22m, poultry.Amount);

        Assert.Equal("MEGAPROMET d.o.o", invoice.Result.MerchantName);
        Assert.Equal("02365928", invoice.Result.MerchantTaxId);
        Assert.Equal(Stage, invoice.Result.Provenance[ExtractedValues.MerchantName]);

        // The request the portal was asked, on the wire: form-encoded, and with the timestamp's
        // offset intact rather than turned into a space by a form encoder.
        var request = Assert.Single(portal.Requests);
        Assert.Equal(DecoderRegressionTests.Ikof, request["iic"]);
        Assert.Equal("02365928", request["tin"]);
        Assert.Equal("2026-08-29T14:59:22+02:00", request["dateTimeCreated"]);
    }

    [Fact]
    public async Task The_list_price_and_the_rebate_reach_the_discount_check_intact()
    {
        await using var portal = await FiscalPortalStub.Answering();
        await using var services = Services(portal);

        var invoice = await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, Decoded);

        // `unitPriceAfterVat` is a unit price and the check it feeds is a line-level one, so it
        // arrives extended by the quantity: 15.00 per kilogram over 0.548 kg is a list price of
        // 8.22 against an amount of 8.22, and the check the design promised keeps working (D27).
        var poultry = invoice!.Result.Candidates[3];
        Assert.Equal(15.00m, poultry.UnitPrice);
        Assert.Equal(8.22m, poultry.ListUnitPrice);
        Assert.Equal(0m, poultry.DiscountAmount);

        var report = ArithmeticValidator.Validate(ExtractionArithmetic.From(invoice.Result));
        Assert.True(report.Passed, string.Join("; ", report.Failures.Select(check => check.Description)));
    }

    [Fact]
    public async Task The_missing_identifier_arrives_from_the_verification_service()
    {
        await using var portal = await FiscalPortalStub.Answering();
        await using var services = Services(portal);

        var invoice = await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, Decoded);

        // The JIKR the fiscal code does not carry. It is the portal's `fic`, and this is the only
        // place it is ever known from (D24).
        Assert.Equal("d2857c6a-a363-4173-bf9c-dff37f77741a", invoice?.Identifiers.Jikr);
        Assert.Equal(DecoderRegressionTests.Ikof, invoice?.Identifiers.Ikof);
    }

    [Fact]
    public async Task The_service_has_no_record_of_the_invoice()
    {
        await using var portal = await FiscalPortalStub.WithNoRecord();
        await using var services = Services(portal);

        Assert.Null(await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, Decoded));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task The_service_answers_with_a_failure(HttpStatusCode status)
    {
        await using var portal = await FiscalPortalStub.Failing(status);
        await using var services = Services(portal);

        // Nothing rather than an exception: a stage that produced nothing is an ordinary outcome,
        // and a government portal having a bad day is not a problem with the receipt (D26).
        Assert.Null(await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, Decoded));
    }

    [Fact]
    public async Task The_service_cannot_be_reached()
    {
        await using var portal = await FiscalPortalStub.Hanging();
        await using var services = Services(portal, ("Extraction:Portal:TimeoutMilliseconds", "250"));

        Assert.Null(await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, Decoded));
    }

    [Fact]
    public async Task Retrieval_is_not_repeated_needlessly()
    {
        await using var portal = await FiscalPortalStub.Answering();
        await using var services = Services(portal);
        await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, Decoded);

        // Resolved afresh, as a second extraction run would: the answer outlives the call that
        // obtained it, not merely the object that made it.
        var again = await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, Decoded);

        // Re-running extraction asks the portal nothing it has already answered.
        Assert.NotNull(again);
        Assert.Single(portal.Requests);
    }

    private ServiceProvider Services(
        FiscalPortalStub portal,
        params (string Key, string Value)[] settings) =>
        postgres.Services([("Extraction:Portal:BaseAddress", portal.BaseAddress), .. settings]);
}
