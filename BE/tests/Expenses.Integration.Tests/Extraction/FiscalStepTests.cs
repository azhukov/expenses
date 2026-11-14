using System.Net;
using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Integration.Tests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Extraction;

/// <summary>
/// The fiscal step's diagnosis, on each path through it, wired as production wires it (D44).
///
/// Scenarios from receipt-ingestion, "Why no invoice was obtained is diagnosed": "No payload at
/// all", "A code that is not a fiscal code", and the verification service's own diagnosis passed
/// through unchanged.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FiscalStepTests(PostgresFixture postgres)
{
    /// <summary>The Megapromet receipt's own code: every parameter the verification service needs.</summary>
    public const string FiscalPayload =
        "https://mapr.tax.gov.me/ic/#/verify?iic=32AA324CFF5030271E16D59F7F8EF636&tin=02365928"
        + "&crtd=2026-08-29T14:59:22+02:00&ord=1&bu=xx&cr=yy&sw=zz&prc=59.65";

    [Fact]
    public async Task No_payload_at_all()
    {
        await using var portal = await FiscalPortalStub.Answering();
        await using var services = Services(portal);

        var result = await Fiscal(services).Run(new ExtractionStepRequest(null, FiscalIdentifiers.None));

        Assert.Equal(FiscalDiagnosisKind.NoPayload, result?.Diagnosis?.Kind);
        Assert.False(result?.ProducedLines);
        Assert.Empty(portal.Requests);
    }

    [Fact]
    public async Task A_decoder_that_throws_reports_no_payload()
    {
        await using var portal = await FiscalPortalStub.Answering();
        await using var services = Services(portal);

        // Bytes that are not an image at all, so the decoder cannot even load them.
        var result = await Fiscal(services).Run(new ExtractionStepRequest(
            new ReceiptImageContent(1, "image/png", [0x01, 0x02, 0x03]),
            FiscalIdentifiers.None));

        Assert.Equal(FiscalDiagnosisKind.NoPayload, result?.Diagnosis?.Kind);
    }

    [Fact]
    public async Task A_code_that_is_not_a_fiscal_code()
    {
        await using var portal = await FiscalPortalStub.Answering();
        await using var services = Services(portal);

        var result = await Fiscal(services).Run(
            new ExtractionStepRequest(null, FiscalIdentifiers.None, "https://example.com/menu?table=4"));

        Assert.Equal(FiscalDiagnosisKind.NotFiscal, result?.Diagnosis?.Kind);

        // The payload is still carried, so that a parser which later learns the format can read it (D32).
        Assert.Equal("https://example.com/menu?table=4", result?.Payload);
        Assert.Empty(portal.Requests);
    }

    [Fact]
    public async Task The_verification_services_diagnosis_is_passed_through()
    {
        await using var portal = await FiscalPortalStub.Failing(HttpStatusCode.InternalServerError);
        await using var services = Services(portal);

        var result = await Fiscal(services).Run(
            new ExtractionStepRequest(null, FiscalIdentifiers.None, FiscalPayload));

        Assert.Equal(FiscalDiagnosisKind.PortalRejected, result?.Diagnosis?.Kind);
        Assert.Equal(500, result?.Diagnosis?.PortalStatus);

        // What the code carried still travels forward without the invoice (D23).
        Assert.Equal("02365928", result?.Fiscal.IssuerTaxNumber);
    }

    [Fact]
    public async Task A_retrieved_invoice_says_so()
    {
        await using var portal = await FiscalPortalStub.Answering();
        await using var services = Services(portal);

        var result = await Fiscal(services).Run(
            new ExtractionStepRequest(null, FiscalIdentifiers.None, FiscalPayload));

        Assert.Equal(FiscalDiagnosisKind.Retrieved, result?.Diagnosis?.Kind);
        Assert.True(result?.ProducedLines);
    }

    [Fact]
    public async Task An_answer_that_fails_while_being_mapped_is_unreadable()
    {
        // Readable JSON whose line has an empty name, which no candidate line may have, so the
        // mapping itself throws rather than the deserialisation.
        await using var portal = await FiscalPortalStub.AnsweringWith(
            """{"fic":"abc","totalPrice":1.00,"items":[{"name":"","quantity":1,"priceAfterVat":1.00,"unitPriceAfterVat":1.00}]}""");
        await using var services = Services(portal);

        var result = await Fiscal(services).Run(
            new ExtractionStepRequest(null, FiscalIdentifiers.None, FiscalPayload));

        Assert.Equal(FiscalDiagnosisKind.UnparseableResponse, result?.Diagnosis?.Kind);
        Assert.False(result?.ProducedLines);
    }

    private static IExtractionStep Fiscal(IServiceProvider services)
        => services.GetServices<IExtractionStep>().Single(step => step.Name == "fiscal");

    private ServiceProvider Services(FiscalPortalStub portal)
        => postgres.Services([
            ("Extraction:Portal:BaseAddress", portal.BaseAddress),
            ("Extraction:Vision:Engine", "placeholder"),
        ]);
}
