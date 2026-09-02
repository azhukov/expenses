using Expenses.Application.Purchases;
using Expenses.Application.Receipts;
using Expenses.Domain;
using Expenses.Integration.Tests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Extraction;

/// <summary>
/// The whole journey for a real photographed receipt: uploaded, decoded, retrieved, reconciled, and
/// waiting as candidates — and, for the receipt whose symbol no decoder reads, the same journey
/// ending at the placeholder with nothing said about the difference.
///
/// Scenarios from receipt-ingestion: "A deterministic result ends the cascade", "The placeholder
/// does not run behind a retrieved invoice", "Invoice detail is retrieved and mapped",
/// "A miss does not degrade the receipt".
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FiscalJourneyTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTime Occurred = new(2041, 3, 4, 10, 15, 0, DateTimeKind.Unspecified);

    private static int _sequence;

    public Task InitializeAsync() => postgres.Migrate();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_receipt_whose_code_decodes_is_extracted_from_the_invoice_itself()
    {
        await using var portal = await FiscalPortalStub.Answering();
        await using var services = postgres.Services(("Extraction:Portal:BaseAddress", portal.BaseAddress));
        using var scope = services.CreateScope();

        var (receipt, view) = await Extract(scope, DecoderRegressionTests.DecodableReceipt);

        Assert.Equal(Receipt.ExtractionState.Extracted, receipt.State);
        Assert.True(view.Validation?.Passed);

        // The invoice the tax authority holds, verbatim: every line, the merchant, and a total that
        // the lines reconcile to only because they are compared at the precision they are stated
        // in (D25, D27).
        Assert.Equal(FiscalPortalTests.Stage, view.Result?.EngineName);
        Assert.Equal(14, view.Result?.Candidates.Count);
        Assert.Equal(59.65m, view.Result?.Total);
        Assert.Equal("MEGAPROMET d.o.o", view.Result?.MerchantName);
        Assert.Equal("02365928", view.Result?.MerchantTaxId);
        Assert.Equal("CABERNET SYRAH 0.75L", view.Result?.Candidates[0].Description);

        // Not one placeholder line, and not one placeholder stage: a probabilistic stage is never
        // asked for a value a deterministic one has already established (D22).
        Assert.Equal(["fiscal-qr", FiscalPortalTests.Stage], view.Result?.StagesRun);
        Assert.All(
            view.Result!.Candidates,
            candidate => Assert.Equal(FiscalPortalTests.Stage, candidate.Provenance["amount"]));

        // The identity the code carried, completed by the one only the service knows (D24).
        Assert.Equal(DecoderRegressionTests.Ikof, receipt.FiscalIkofExtracted);
        Assert.Equal("d2857c6a-a363-4173-bf9c-dff37f77741a", receipt.FiscalJikrExtracted);
        Assert.Equal(Receipt.FiscalSource.RetrievedFromService, receipt.FiscalExtractedSource);
    }

    [Fact]
    public async Task A_miss_is_reported_no_differently_from_a_receipt_carrying_no_code()
    {
        await using var portal = await FiscalPortalStub.Answering();
        await using var services = postgres.Services(("Extraction:Portal:BaseAddress", portal.BaseAddress));
        using var scope = services.CreateScope();

        var (receipt, view) = await Extract(scope, DecoderRegressionTests.UndecodableReceipt);

        // The placeholder path, reached exactly as it is for an image with no symbol on it at all,
        // and the portal never asked about an invoice nobody could name.
        Assert.Equal(Receipt.ExtractionState.Extracted, receipt.State);
        Assert.Equal("placeholder", view.Result?.EngineName);
        Assert.Contains("vision-cheap", view.Result!.StagesRun);
        Assert.Empty(portal.Requests);

        Assert.Null(receipt.FiscalIkofExtracted);
        Assert.Null(receipt.FailureReason);
        Assert.Equal(Receipt.FiscalSource.None, receipt.FiscalExtractedSource);
        Assert.Equal(Receipt.FiscalCorroboration.Absent, receipt.Corroboration);
    }

    private static async Task<(ReceiptView Receipt, ExtractionView View)> Extract(
        IServiceScope scope,
        string fixture)
    {
        var purchase = await scope.ServiceProvider.GetRequiredService<RecordPurchase>().Execute(
            new RecordPurchaseCommand(Next(), 59.65m, [new ExpenseCommand("Receipt", 59.65m)]));

        await scope.ServiceProvider.GetRequiredService<AttachReceiptImage>()
            .Execute(purchase.Purchase.Id, DecoderRegressionTests.Photograph(fixture).Content);

        var receipt = await scope.ServiceProvider.GetRequiredService<RunExtraction>()
            .Execute(purchase.Purchase.Id);

        var view = await scope.ServiceProvider.GetRequiredService<GetExtractionCandidates>()
            .Execute(purchase.Purchase.Id);

        return (receipt, view);
    }

    private static DateTime Next() => Occurred.AddMinutes(Interlocked.Increment(ref _sequence));
}
