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

    /// <summary>
    /// The portal states weighed lines unrounded (1.696 kg × 2.30 = 3.9008) and rounds only the
    /// invoice's totals. The ledger keeps money to the cent (D7), so every money value arrives in
    /// cents, as the receipt prints it.
    /// </summary>
    [Fact]
    public async Task Weighed_lines_arrive_in_cents_as_the_receipt_prints_them()
    {
        await using var portal = await FiscalPortalStub.Answering();
        await using var services = Services(portal);

        var invoice = (await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded)).Invoice;

        Assert.NotNull(invoice);
        var tomatoes = invoice.Candidates[7];
        Assert.Equal("PARADAJZ rozi", tomatoes.Description);
        Assert.Equal(3.90m, tomatoes.Amount);
        Assert.Equal(3.90m, tomatoes.ListUnitPrice);

        // Quantities are not money, and keep their three places.
        Assert.Equal(1.696m, tomatoes.Quantity);

        Assert.All(invoice.Candidates, line =>
        {
            Assert.Equal(line.Amount, CentRounding.ToCents(line.Amount!.Value));
            Assert.Equal(line.UnitPrice, line.UnitPrice is { } unit ? CentRounding.ToCents(unit) : null);
            Assert.Equal(line.ListUnitPrice, line.ListUnitPrice is { } list ? CentRounding.ToCents(list) : null);
            Assert.Equal(line.DiscountAmount, line.DiscountAmount is { } off ? CentRounding.ToCents(off) : null);
        });
        Assert.Equal(59.65m, invoice.Candidates.Sum(line => line.Amount));
        Assert.Equal(invoice.Total, invoice.Candidates.Sum(line => line.Amount));
    }

    [Fact]
    public async Task Lines_that_round_past_the_total_are_brought_back_to_it()
    {
        // Three lines of 0.335, 0.335 and 0.33: each rounds up, to 1.01, against a stated 1.00.
        await using var portal = await FiscalPortalStub.AnsweringWith("""
            {"fic":"abc","totalPrice":1.00,"items":[
              {"name":"A","quantity":1,"unitPriceAfterVat":0.335,"priceAfterVat":0.335,"rebate":0,"vatRate":21},
              {"name":"B","quantity":1,"unitPriceAfterVat":0.335,"priceAfterVat":0.335,"rebate":0,"vatRate":21},
              {"name":"C","quantity":1,"unitPriceAfterVat":0.33,"priceAfterVat":0.33,"rebate":0,"vatRate":21}]}
            """);
        await using var services = Services(portal);

        var invoice = (await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded)).Invoice;

        Assert.NotNull(invoice);
        Assert.Equal([0.33m, 0.34m, 0.33m], invoice.Candidates.Select(line => line.Amount));

        // The cent taken off a line is taken off its list price too, so the line still adds up.
        Assert.Equal([0.33m, 0.34m, 0.33m], invoice.Candidates.Select(line => line.ListUnitPrice));
        Assert.Equal(1.00m, invoice.Candidates.Sum(line => line.Amount));

        var report = ArithmeticValidator.Validate(ExtractionArithmetic.From(invoice));
        Assert.True(report.Passed, string.Join("; ", report.Failures.Select(check => check.Description)));
    }

    /// <summary>
    /// The portal's <c>rebate</c> is a percentage (the schema's <c>R</c>), not an amount, and
    /// <c>unitPriceAfterVat</c> is the price before it. Taken as an amount, a 10% rebate on 5.00
    /// would list 5.00 less 10.00 against an amount of 4.50, and fail the discount check on an
    /// authoritative invoice.
    /// </summary>
    [Fact]
    public async Task A_rebate_is_a_percentage_and_arrives_as_the_amount_taken_off()
    {
        await using var portal = await FiscalPortalStub.AnsweringWith("""
            {"fic":"abc","totalPrice":7.62,"items":[
              {"name":"A","quantity":1,"unitPriceAfterVat":5.00,"priceAfterVat":4.50,"rebate":10,"vatRate":21},
              {"name":"B","quantity":1.696,"unitPriceAfterVat":2.30,"priceAfterVat":3.12064,"rebate":20,"vatRate":21}]}
            """);
        await using var services = Services(portal);

        var invoice = (await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded)).Invoice;

        Assert.NotNull(invoice);
        Assert.Equal([4.50m, 3.12m], invoice.Candidates.Select(line => line.Amount));
        Assert.Equal([5.00m, 3.90m], invoice.Candidates.Select(line => line.ListUnitPrice));
        Assert.Equal([0.50m, 0.78m], invoice.Candidates.Select(line => line.DiscountAmount));

        var report = ArithmeticValidator.Validate(ExtractionArithmetic.From(invoice));
        Assert.True(report.Passed, string.Join("; ", report.Failures.Select(check => check.Description)));
    }

    /// <summary>
    /// The Aroma receipt of 3 September 2026 (Broj 2619, 35.11): four lines printed with a
    /// "Cijena … Popust …%" (list price, discount) beside weighed lines and plain ones. The portal
    /// states each discounted line's unit price before the discount, the discount as a percentage,
    /// and the amount after it unrounded (4.19 less 22.43% is 3.250183). What arrives is what the
    /// till printed: the amount, the list price, and the discount in euros.
    /// </summary>
    [Fact]
    public async Task A_receipt_with_popust_lines_arrives_as_the_till_printed_it()
    {
        await using var portal = await FiscalPortalStub.AnsweringWith("""
            {"fic":"bd6f241e-7a6b-4a36-80c8-c593f25f17e6","totalPrice":35.11,"totalVATAmount":5.68,
             "seller":{"idNum":"02440261","name":"DOMACA TRGOVINA doo"},
             "sameTaxes":[{"vatRate":21,"vatAmount":5.42},{"vatRate":7,"vatAmount":0.25}],
             "items":[
              {"name":"Cokolada crna Galeb 100g 80% kakao","unit":"KOM","quantity":1,"unitPriceAfterVat":4.19,"priceAfterVat":3.250183,"rebate":22.43,"rebateReducing":false,"vatRate":21},
              {"name":"Cokolada crna Galeb 100g 80% kakao","unit":"KOM","quantity":1,"unitPriceAfterVat":4.19,"priceAfterVat":3.250183,"rebate":22.43,"rebateReducing":false,"vatRate":21},
              {"name":"Lubenica","unit":"KG","quantity":3.344,"unitPriceAfterVat":0.65,"priceAfterVat":2.1736,"rebate":0,"vatRate":21},
              {"name":"Mlijeko Milkos sterilno sa cepom 2.8%mm","unit":"KOM","quantity":1,"unitPriceAfterVat":0.89,"priceAfterVat":0.89,"rebate":0,"vatRate":7},
              {"name":"Vino crveno Plantaze Epoha 0.75L","unit":"KOM","quantity":1,"unitPriceAfterVat":11.49,"priceAfterVat":11.49,"rebate":0,"vatRate":21},
              {"name":"Pavlaka kisjela Moja Kravica 20%mm 400g","unit":"KOM","quantity":1,"unitPriceAfterVat":1.75,"priceAfterVat":1.48995,"rebate":14.86,"rebateReducing":false,"vatRate":7},
              {"name":"Sirni namaz Ala kajmak 150g","unit":"KOM","quantity":1,"unitPriceAfterVat":2.39,"priceAfterVat":2.090055,"rebate":12.55,"rebateReducing":false,"vatRate":21},
              {"name":"Hljeb farmerski Aroma 400g","unit":"KOM","quantity":1,"unitPriceAfterVat":1.49,"priceAfterVat":1.49,"rebate":0,"vatRate":7},
              {"name":"Sir olives&tomatoes 200g","unit":"KOM","quantity":1,"unitPriceAfterVat":4.09,"priceAfterVat":4.09,"rebate":0,"vatRate":21},
              {"name":"Pileca virsla classico mini Wudy 125g","unit":"KOM","quantity":1,"unitPriceAfterVat":1.05,"priceAfterVat":0.589995,"rebate":43.81,"rebateReducing":false,"vatRate":21},
              {"name":"Jabuka zlatni delises","unit":"KG","quantity":1.31,"unitPriceAfterVat":1.79,"priceAfterVat":2.3449,"rebate":0,"vatRate":21},
              {"name":"Krompir mladi","unit":"KG","quantity":1.534,"unitPriceAfterVat":0.99,"priceAfterVat":1.51866,"rebate":0,"vatRate":21},
              {"name":"Crni luk","unit":"KG","quantity":0.524,"unitPriceAfterVat":0.85,"priceAfterVat":0.4454,"rebate":0,"vatRate":21}]}
            """);
        await using var services = Services(portal);

        var invoice = (await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded)).Invoice;

        Assert.NotNull(invoice);
        Assert.Equal(13, invoice.Candidates.Count);
        Assert.Equal(
            [3.25m, 3.25m, 2.17m, 0.89m, 11.49m, 1.49m, 2.09m, 1.49m, 4.09m, 0.59m, 2.34m, 1.52m, 0.45m],
            invoice.Candidates.Select(line => line.Amount));
        Assert.Equal(35.11m, invoice.Total);
        Assert.Equal(invoice.Total, invoice.Candidates.Sum(line => line.Amount));
        Assert.Equal(5.68m, invoice.TaxAmount);

        // Two tax rates on one invoice: neither is the invoice's rate.
        Assert.Null(invoice.TaxRatePercent);

        // The popust lines, as printed: "Cijena: 4.19, Popust: 22.43% (0.94Eur)" and the rest.
        (int Line, decimal ListPrice, decimal Discount, decimal Percent)[] popust =
        [
            (1, 4.19m, 0.94m, 22.43m),
            (2, 4.19m, 0.94m, 22.43m),
            (6, 1.75m, 0.26m, 14.86m),
            (7, 2.39m, 0.30m, 12.55m),
            (10, 1.05m, 0.46m, 43.81m),
        ];

        foreach (var (line, listPrice, discount, percent) in popust)
        {
            var candidate = invoice.Candidates[line - 1];
            Assert.Equal(listPrice, candidate.ListUnitPrice);
            Assert.Equal(discount, candidate.DiscountAmount);
            Assert.Equal(listPrice - discount, candidate.Amount);

            // The percentage is derived for display, never stored (D19), and derived from the
            // amounts it comes back to the one the till printed.
            Assert.Equal(percent, Math.Round(discount / listPrice * 100m, 2));
        }

        // Every other line, weighed or not, is undiscounted: its list price is its amount.
        var popustLines = popust.Select(entry => entry.Line).ToHashSet();
        Assert.All(
            invoice.Candidates.Where(candidate => !popustLines.Contains(candidate.LineNumber)),
            candidate =>
            {
                Assert.Equal(0m, candidate.DiscountAmount);
                Assert.Equal(candidate.Amount, candidate.ListUnitPrice);
            });

        // A weighed line keeps its three places of quantity: 3.344 kg × 0.65 is 2.17.
        Assert.Equal(3.344m, invoice.Candidates[2].Quantity);

        var report = ArithmeticValidator.Validate(ExtractionArithmetic.From(invoice));
        Assert.True(report.Passed, string.Join("; ", report.Failures.Select(check => check.Description)));
    }

    /// <summary>
    /// IDEA's till states no rebate at all where Megapromet's states a zero one: the portal answers
    /// <c>"rebate": null</c>. Recorded from the real portal for an IDEA receipt of 1.19 (one carton
    /// of sour cream at 7%). A field the invoice leaves out is not an answer this cannot read.
    /// </summary>
    [Fact]
    public async Task An_invoice_that_states_no_rebate_is_retrieved_and_mapped()
    {
        await using var portal = await FiscalPortalStub.Answering("136329B53A14C42501B9614D8C619CF2");
        await using var services = Services(portal);

        var retrieval = await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, Idea(
            "136329B53A14C42501B9614D8C619CF2", "2026-09-09T21:24:41+02:00", 1.19m));
        var invoice = retrieval.Invoice;

        Assert.Equal(FiscalDiagnosisKind.Retrieved, retrieval.Diagnosis.Kind);
        Assert.NotNull(invoice);

        var line = Assert.Single(invoice.Candidates);
        Assert.Equal("PAVLAKA KISJELA 20%MM 180ML IMLEK", line.Description);
        Assert.Equal(1.19m, line.Amount);
        Assert.Equal(1m, line.Quantity);
        Assert.Equal(7m, line.TaxRatePercent);

        // No rebate stated is no discount: the list price is the amount, and nothing is taken off.
        Assert.Equal(1.19m, line.ListUnitPrice);
        Assert.Equal(0m, line.DiscountAmount);

        Assert.Equal(1.19m, invoice.Total);
        Assert.Equal(0.08m, invoice.TaxAmount);
        Assert.Equal(7m, invoice.TaxRatePercent);
        Assert.Equal("IDEA CG", invoice.MerchantName);
        Assert.Equal("02793962", invoice.MerchantTaxId);
        Assert.Equal("2c115cd6-dd7b-4337-b681-b07c2e0835ef", invoice.Fiscal.Jikr);

        var report = ArithmeticValidator.Validate(ExtractionArithmetic.From(invoice));
        Assert.True(report.Passed, string.Join("; ", report.Failures.Select(check => check.Description)));
    }

    /// <summary>
    /// The other IDEA receipt, 10.02: one watermelon weighed in two goes, 10 kg and then 0.125 kg at
    /// 0.99. The till prints the second line unrounded as 0,1238 and the total as 10,02, and the
    /// portal states it the same way, with no rebate on either line.
    /// </summary>
    [Fact]
    public async Task A_weighed_invoice_that_states_no_rebate_arrives_in_cents()
    {
        await using var portal = await FiscalPortalStub.Answering("C92945B94C4DFCA0EE9A13819682C03D");
        await using var services = Services(portal);

        var retrieval = await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, Idea(
            "C92945B94C4DFCA0EE9A13819682C03D", "2026-09-09T13:24:42+02:00", 10.02m));
        var invoice = retrieval.Invoice;

        Assert.Equal(FiscalDiagnosisKind.Retrieved, retrieval.Diagnosis.Kind);
        Assert.NotNull(invoice);
        Assert.Equal(["LUBENICA M", "LUBENICA M"], invoice.Candidates.Select(line => line.Description));
        Assert.Equal([10m, 0.125m], invoice.Candidates.Select(line => line.Quantity));
        Assert.Equal([9.90m, 0.12m], invoice.Candidates.Select(line => line.Amount));
        Assert.Equal([9.90m, 0.12m], invoice.Candidates.Select(line => line.ListUnitPrice));
        Assert.Equal([0m, 0m], invoice.Candidates.Select(line => line.DiscountAmount));

        Assert.Equal(10.02m, invoice.Total);
        Assert.Equal(invoice.Total, invoice.Candidates.Sum(line => line.Amount));
        Assert.Equal(1.74m, invoice.TaxAmount);
        Assert.Equal(21m, invoice.TaxRatePercent);
        Assert.Equal("c8b5f6d8-1f5c-45e0-95ca-733100152e39", invoice.Fiscal.Jikr);

        var report = ArithmeticValidator.Validate(ExtractionArithmetic.From(invoice));
        Assert.True(report.Passed, string.Join("; ", report.Failures.Select(check => check.Description)));
    }

    /// <summary>
    /// A line the portal states with no amount cannot become a ledger line, and the answer is not
    /// taken; but it is still a diagnosis, not an exception.
    /// </summary>
    [Fact]
    public async Task A_line_with_no_amount_is_an_answer_that_cannot_be_taken()
    {
        await using var portal = await FiscalPortalStub.AnsweringWith("""
            {"fic":"abc","totalPrice":1.00,"items":[
              {"name":"A","quantity":null,"unitPriceAfterVat":null,"priceAfterVat":null,"rebate":null,"vatRate":null}]}
            """);
        await using var services = Services(portal);

        var retrieval = await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded);

        Assert.Null(retrieval.Invoice);
        Assert.Equal(FiscalDiagnosisKind.UnparseableResponse, retrieval.Diagnosis.Kind);
    }

    private static FiscalIdentifiers Idea(string ikof, string createdAt, decimal total)
        => new(ikof, Jikr: null, IssuerTaxNumber: "02793962", CreatedAt: createdAt, Total: total);

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
    public async Task A_refused_request_reports_the_request_that_was_sent()
    {
        await using var portal = await FiscalPortalStub.Failing(HttpStatusCode.InternalServerError);
        await using var services = Services(portal);

        var retrieval = await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded);

        // What went over the wire, so the person holding the receipt can see what the site was asked.
        var request = retrieval.Diagnosis.PortalRequest;
        Assert.NotNull(request);
        Assert.Equal("POST", request.Method);
        Assert.Equal(new Uri(new Uri(portal.BaseAddress), "/ic/api/verifyInvoice").ToString(), request.Url);
        Assert.Equal(DecoderRegressionTests.Ikof, request.Form["iic"]);
        Assert.Equal("02365928", request.Form["tin"]);
        Assert.Equal("2026-08-29T14:59:22+02:00", request.Form["dateTimeCreated"]);
        Assert.Equal(Assert.Single(portal.Requests), request.Form);
    }

    [Fact]
    public async Task Every_request_that_was_sent_is_reported()
    {
        await using var noRecord = await FiscalPortalStub.WithNoRecord();
        await using var garbled = await FiscalPortalStub.AnsweringWith("not json");
        await using var hanging = await FiscalPortalStub.Hanging();
        await using var answering = await FiscalPortalStub.Answering();

        foreach (var portal in new[] { noRecord, garbled, hanging, answering })
        {
            await using var services = Services(portal, ("Extraction:Portal:TimeoutMilliseconds", "250"));

            var retrieval = await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, s_decoded);

            Assert.Equal(DecoderRegressionTests.Ikof, retrieval.Diagnosis.PortalRequest?.Form["iic"]);
        }
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

        // Nothing was sent, so there is no request to report.
        Assert.Null(retrieval.Diagnosis.PortalRequest);
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
