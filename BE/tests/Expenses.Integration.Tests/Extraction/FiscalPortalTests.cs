using System.Net;
using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Application.Services;
using Expenses.Integration.Tests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Extraction;

/// <summary>
/// Retrieval from the fiscal verification service, against the response recorded once from the real
/// portal and committed with the photographs (D26). Nothing here reaches the network.
///
/// Scenarios from receipt-ingestion: "Invoice detail is retrieved and mapped", "The missing
/// identifier arrives from the verification service", "The service has no record of the invoice",
/// "The service cannot be reached", "Retrieval is not repeated needlessly", and the diagnosis
/// each of them reports: "The service refuses the request", "The service answers in an unreadable
/// shape", "A fiscal code missing parameters" (D43).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FiscalPortalTests(PostgresFixture postgres)
{
    public const string Stage = "fiscal-portal";

    /// <summary>What the Megapromet receipt's own QR carries, and all the portal is given.</summary>
    private static readonly FiscalIdentifiers s_decoded = new(
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

        var retrieval = await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded);
        var invoice = retrieval.Invoice;

        Assert.Equal(FiscalDiagnosisKind.Retrieved, retrieval.Diagnosis.Kind);
        Assert.NotNull(invoice);
        Assert.Equal(7, invoice.PurchaseId);
        Assert.Equal(14, invoice.Candidates.Count);
        Assert.Equal(59.65m, invoice.Total);

        // Verbatim, all of it: the tax authority has already stated these and nothing here is
        // entitled to a second opinion (D27).
        var wine = invoice.Candidates[0];
        Assert.Equal("CABERNET SYRAH 0.75L", wine.Description);
        Assert.Equal(1m, wine.Quantity);
        Assert.Equal("KOM", wine.UnitRaw);
        Assert.Equal(9.75m, wine.Amount);
        Assert.Equal(Stage, wine.Provenance[ExtractedValues.Amount]);

        // A line priced by weight: the quantity is not a whole number and the amount is not the
        // unit price, which is exactly the case a mapping is most likely to get wrong.
        var poultry = invoice.Candidates[3];
        Assert.Equal("CURECI FILE svjezi", poultry.Description);
        Assert.Equal(0.548m, poultry.Quantity);
        Assert.Equal("KG", poultry.UnitRaw);
        Assert.Equal(8.22m, poultry.Amount);

        Assert.Equal("MEGAPROMET d.o.o", invoice.MerchantName);
        Assert.Equal("02365928", invoice.MerchantTaxId);
        Assert.Equal(Stage, invoice.Provenance[ExtractedValues.MerchantName]);

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

        var invoice = (await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded)).Invoice;

        // `unitPriceAfterVat` is a unit price and the check it feeds is a line-level one, so it
        // arrives extended by the quantity: 15.00 per kilogram over 0.548 kg is a list price of
        // 8.22 against an amount of 8.22, and the check the design promised keeps working (D27).
        Assert.NotNull(invoice);
        var poultry = invoice.Candidates[3];
        Assert.Equal(15.00m, poultry.UnitPrice);
        Assert.Equal(8.22m, poultry.ListUnitPrice);
        Assert.Equal(0m, poultry.DiscountAmount);

        var report = ArithmeticValidator.Validate(ExtractionArithmetic.From(invoice));
        Assert.True(report.Passed, string.Join("; ", report.Failures.Select(check => check.Description)));
    }

    [Fact]
    public async Task The_missing_identifier_arrives_from_the_verification_service()
    {
        await using var portal = await FiscalPortalStub.Answering();
        await using var services = Services(portal);

        var invoice = (await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded)).Invoice;

        // The JIKR the fiscal code does not carry. It is the portal's `fic`, and this is the only
        // place it is ever known from (D24).
        Assert.Equal("d2857c6a-a363-4173-bf9c-dff37f77741a", invoice?.Fiscal.Jikr);
        Assert.Equal(DecoderRegressionTests.Ikof, invoice?.Fiscal.Ikof);
    }

    [Fact]
    public async Task The_service_has_no_record_of_the_invoice()
    {
        await using var portal = await FiscalPortalStub.WithNoRecord();
        await using var services = Services(portal);

        var retrieval = await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded);

        Assert.Null(retrieval.Invoice);
        Assert.Equal(FiscalDiagnosisKind.NoRecord, retrieval.Diagnosis.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task The_service_refuses_the_request(HttpStatusCode status)
    {
        await using var portal = await FiscalPortalStub.Failing(status);
        await using var services = Services(portal);

        // Nothing rather than an exception: a stage that produced nothing is an ordinary outcome,
        // and a government portal having a bad day is not a problem with the receipt (D26). Which
        // status it answered is still worth telling the person holding the receipt (D43).
        var retrieval = await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded);

        Assert.Null(retrieval.Invoice);
        Assert.Equal(FiscalDiagnosisKind.PortalRejected, retrieval.Diagnosis.Kind);
        Assert.Equal((int)status, retrieval.Diagnosis.PortalStatus);
    }

    [Fact]
    public async Task The_service_answers_in_an_unreadable_shape()
    {
        // An error page where JSON was expected, long enough that an untruncated excerpt would show.
        string body = "<html><body>" + new string('x', 500) + "</body></html>";
        await using var portal = await FiscalPortalStub.AnsweringWith(body);
        await using var services = Services(portal);

        var retrieval = await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded);

        Assert.Null(retrieval.Invoice);
        Assert.Equal(FiscalDiagnosisKind.UnparseableResponse, retrieval.Diagnosis.Kind);
        Assert.NotNull(retrieval.Diagnosis.ResponseExcerpt);
        Assert.StartsWith("<html><body>xxx", retrieval.Diagnosis.ResponseExcerpt, StringComparison.Ordinal);
        Assert.Equal(FiscalDiagnosis.MaximumExcerptLength, retrieval.Diagnosis.ResponseExcerpt.Length);
    }

    [Fact]
    public async Task An_invoice_with_no_lines_is_an_unreadable_answer()
    {
        await using var portal = await FiscalPortalStub.AnsweringWith("""{"fic":"abc","totalPrice":1.00,"items":[]}""");
        await using var services = Services(portal);

        var retrieval = await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded);

        Assert.Null(retrieval.Invoice);
        Assert.Equal(FiscalDiagnosisKind.UnparseableResponse, retrieval.Diagnosis.Kind);
        Assert.Contains("\"items\":[]", retrieval.Diagnosis.ResponseExcerpt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_service_cannot_be_reached()
    {
        await using var portal = await FiscalPortalStub.Hanging();
        await using var services = Services(portal, ("Extraction:Portal:TimeoutMilliseconds", "250"));

        var retrieval = await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded);

        Assert.Null(retrieval.Invoice);
        Assert.Equal(FiscalDiagnosisKind.PortalUnreachable, retrieval.Diagnosis.Kind);
        Assert.Contains("timed out", retrieval.Diagnosis.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_refused_connection_is_unreachable()
    {
        // A stub that has been stopped leaves an address nothing listens on.
        string address;
        await using (var stopped = await FiscalPortalStub.Answering())
        {
            address = stopped.BaseAddress;
        }

        await using var services = postgres.Services(
            [("Extraction:Portal:BaseAddress", address), ("Extraction:Portal:TimeoutMilliseconds", "10000")]);

        var retrieval = await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded);

        Assert.Null(retrieval.Invoice);
        Assert.Equal(FiscalDiagnosisKind.PortalUnreachable, retrieval.Diagnosis.Kind);
        Assert.Contains("could not be reached", retrieval.Diagnosis.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, false, "tin,crtd")]
    [InlineData(false, true, true, "iic")]
    [InlineData(true, true, false, "crtd")]
    public async Task A_fiscal_code_missing_parameters_is_not_asked_about(
        bool hasIic,
        bool hasTin,
        bool hasCreated,
        string missing)
    {
        await using var portal = await FiscalPortalStub.Answering();
        await using var services = Services(portal);
        var partial = s_decoded with
        {
            Ikof = hasIic ? s_decoded.Ikof : null,
            IssuerTaxNumber = hasTin ? s_decoded.IssuerTaxNumber : null,
            CreatedAt = hasCreated ? s_decoded.CreatedAt : null,
        };

        var retrieval = await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, partial);

        Assert.Null(retrieval.Invoice);
        Assert.Equal(FiscalDiagnosisKind.IncompleteIdentifiers, retrieval.Diagnosis.Kind);
        Assert.Equal(missing.Split(','), retrieval.Diagnosis.MissingParameters);
        Assert.Empty(portal.Requests);
    }

    [Fact]
    public async Task A_failure_is_not_remembered()
    {
        await using var portal = await FiscalPortalStub.Failing(HttpStatusCode.InternalServerError);
        await using var services = Services(portal);
        await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded);

        await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded);

        // A later capture is a real second attempt, which is what makes "try again later" advice.
        Assert.Equal(2, portal.Requests.Count);
    }

    [Fact]
    public async Task Retrieval_is_not_repeated_needlessly()
    {
        await using var portal = await FiscalPortalStub.Answering();
        await using var services = Services(portal);
        await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded);

        // Resolved afresh, as a second extraction run would: the answer outlives the call that
        // obtained it, not merely the object that made it.
        var again = await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded);

        // Re-running extraction asks the portal nothing it has already answered.
        Assert.NotNull(again.Invoice);
        Assert.Equal(FiscalDiagnosisKind.Retrieved, again.Diagnosis.Kind);
        Assert.Single(portal.Requests);
    }

    private ServiceProvider Services(
        FiscalPortalStub portal,
        params (string Key, string Value)[] settings)
        => postgres.Services([("Extraction:Portal:BaseAddress", portal.BaseAddress), .. settings]);
}
