using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Application.Services;
using Expenses.Integration.Tests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Extraction;

/// <summary>
/// Two Aroma receipts (DOMACA TRGOVINA doo, PIB 02440261), answered by the real portal on
/// 2026-11-15 and committed as <c>Fixtures/verify-{ikof}.json</c>. Aroma's till states a discounted
/// line differently from Megapromet and IDEA: <c>unitPriceAfterVat</c> is the price <em>after</em>
/// the discount, equal to <c>priceAfterVat</c>, and only <c>unitPriceBeforeVat</c> and
/// <c>rebate</c> still carry the list price (7.0248 × 1.21 = 8.50, less 41.2941% = 4.99). Names are
/// padded with trailing spaces to 40 characters.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AromaFiscalTests(PostgresFixture postgres)
{
    /// <summary>Receipt 3890: one ice cream, printed "Cijena: 8.50, Popust: 41.29% (3.51Eur)".</summary>
    public const string SingleDiscountIkof = "EC071329F224F4B88F3F81F21874499B";

    /// <summary>Receipt 2933: twelve lines, two of them discounted, total 26.76.</summary>
    public const string MixedIkof = "902B5C31D1DB4844C5D9DD6E798CE550";

    private static FiscalIdentifiers Decoded(string ikof, string createdAt, decimal total)
        => new(ikof, Jikr: null, IssuerTaxNumber: "02440261", CreatedAt: createdAt, Total: total);

    private static readonly FiscalIdentifiers s_single = Decoded(SingleDiscountIkof, "2026-10-15T14:30:33+02:00", 4.99m);

    private static readonly FiscalIdentifiers s_mixed = Decoded(MixedIkof, "2026-09-10T17:02:02+02:00", 26.76m);

    [Fact]
    public async Task The_single_line_receipt_is_retrieved()
    {
        var retrieval = await Retrieve(SingleDiscountIkof, s_single);

        Assert.Equal(FiscalDiagnosisKind.Retrieved, retrieval.Diagnosis.Kind);
        var invoice = Assert.IsType<ExtractionStepResult>(retrieval.Invoice);
        var line = Assert.Single(invoice.Candidates);
        Assert.Equal(4.99m, line.Amount);
        Assert.Equal(4.99m, invoice.Total);
        Assert.Equal(0.87m, invoice.TaxAmount);
        Assert.Equal("ffca44fc-8ed9-4eb9-88dd-58c9777061e1", invoice.Fiscal.Jikr);
    }

    [Fact]
    public async Task The_mixed_receipt_is_retrieved_and_sums_to_its_total()
    {
        var invoice = (await Retrieve(MixedIkof, s_mixed)).Invoice;

        Assert.NotNull(invoice);
        Assert.Equal(12, invoice.Candidates.Count);
        Assert.Equal(26.76m, invoice.Candidates.Sum(line => line.Amount));
        Assert.Equal(26.76m, invoice.Total);
    }

    /// <summary>
    /// The receipt prints a list price of 8.50 and a discount of 3.51. The portal still states both
    /// (unitPriceBeforeVat 7.0248 at 21%, rebate 41.2941%), but not in <c>unitPriceAfterVat</c>,
    /// which is the field the mapping reads.
    /// </summary>
    [Fact]
    public async Task The_printed_discount_survives_the_mapping()
    {
        var invoice = (await Retrieve(SingleDiscountIkof, s_single)).Invoice;

        Assert.NotNull(invoice);
        var line = Assert.Single(invoice.Candidates);
        Assert.Equal(8.50m, line.ListUnitPrice);
        Assert.Equal(3.51m, line.DiscountAmount);
    }

    [Fact]
    public async Task Both_discounts_on_the_mixed_receipt_survive_the_mapping()
    {
        var invoice = (await Retrieve(MixedIkof, s_mixed)).Invoice;

        Assert.NotNull(invoice);

        // "Cijena: 8.50, Popust: 53.06% (4.51Eur)" and "Cijena: 7.50, Popust: 53.47% (4.01Eur)".
        Assert.Equal(8.50m, invoice.Candidates[3].ListUnitPrice);
        Assert.Equal(4.51m, invoice.Candidates[3].DiscountAmount);
        Assert.Equal(7.50m, invoice.Candidates[4].ListUnitPrice);
        Assert.Equal(4.01m, invoice.Candidates[4].DiscountAmount);
        Assert.Equal(8.52m, invoice.Candidates.Sum(line => line.DiscountAmount ?? 0m));
    }

    [Fact]
    public async Task The_mapped_invoice_passes_the_arithmetic_check()
    {
        foreach (var (ikof, decoded) in new[] { (SingleDiscountIkof, s_single), (MixedIkof, s_mixed) })
        {
            var invoice = (await Retrieve(ikof, decoded)).Invoice;

            Assert.NotNull(invoice);
            var report = ArithmeticValidator.Validate(ExtractionArithmetic.From(invoice));
            Assert.True(report.Passed, $"{ikof}: " + string.Join("; ", report.Failures.Select(check => check.Description)));
        }
    }

    [Fact]
    public async Task Line_names_arrive_without_the_tills_padding()
    {
        var invoice = (await Retrieve(MixedIkof, s_mixed)).Invoice;

        Assert.NotNull(invoice);
        Assert.Equal("Kesa tregerica Aroma", invoice.Candidates[11].Description);
        Assert.All(invoice.Candidates, line => Assert.Equal(line.Description!.Trim(), line.Description));
    }

    private async Task<FiscalRetrieval> Retrieve(string ikof, FiscalIdentifiers decoded)
    {
        await using var portal = await FiscalPortalStub.Answering(ikof);
        await using var services = postgres.Services([("Extraction:Portal:BaseAddress", portal.BaseAddress)]);

        return await services.GetRequiredService<IFiscalInvoiceRetrieval>().Retrieve(7, decoded);
    }
}
