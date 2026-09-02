using System.Text.Json;
using Expenses.Application.Purchases;
using Expenses.Application.Receipts;
using Expenses.Integration.Tests.Extraction;
using Expenses.Integration.Tests.Harness;

namespace Expenses.Integration.Tests.Mcp;

/// <summary>
/// Where the lines came from, as both interfaces report it. A retrieved invoice and a placeholder's
/// invention are worth entirely different amounts of trust, so telling them apart must not require
/// knowing which stage name means what.
///
/// Scenarios from receipt-ingestion: "Retrieved detail supersedes an estimate", "Stage provenance is
/// recorded", "Placeholder results are identified".
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FiscalSourceReportingTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTime Occurred = new(2042, 6, 7, 11, 45, 0, DateTimeKind.Unspecified);

    private static int _sequence;

    private FiscalPortalStub _portal = null!;

    private ExpensesMcp _mcp = null!;

    public async Task InitializeAsync()
    {
        await postgres.Migrate();
        _portal = await FiscalPortalStub.Answering();
        _mcp = await ExpensesMcp.Start(
            postgres.ConnectionString,
            ("Extraction:Portal:BaseAddress", _portal.BaseAddress));
    }

    public async Task DisposeAsync()
    {
        await _mcp.DisposeAsync();
        await _portal.DisposeAsync();
    }

    [Fact]
    public async Task A_retrieved_invoice_is_reported_as_having_come_from_the_verification_service()
    {
        var purchaseId = await GivenExtracted(DecoderRegressionTests.DecodableReceipt);

        var extraction = await Call(purchaseId);

        Assert.Equal(FiscalPortalTests.Stage, extraction.GetProperty("engineName").GetString());
        Assert.Contains(
            FiscalPortalTests.Stage,
            extraction.GetProperty("stagesRun").EnumerateArray().Select(stage => stage.GetString()));

        // Said in words, not only in a stage name: an assistant relaying this should not have to
        // know that "fiscal-portal" is the tax authority and "placeholder" is a guess.
        Assert.Contains(
            "verification service",
            extraction.GetProperty("summary").GetString()!,
            StringComparison.OrdinalIgnoreCase);

        // And the stage is named against the values it produced, line by line.
        var candidates = extraction.GetProperty("result").GetProperty("candidates").EnumerateArray();
        Assert.All(candidates, candidate => Assert.Equal(
            FiscalPortalTests.Stage,
            candidate.GetProperty("provenance").GetProperty("amount").GetString()));
    }

    [Fact]
    public async Task A_placeholder_result_is_still_reported_as_a_placeholder()
    {
        var purchaseId = await GivenExtracted(DecoderRegressionTests.UndecodableReceipt);

        var extraction = await Call(purchaseId);

        Assert.Equal("placeholder", extraction.GetProperty("engineName").GetString());
        Assert.Contains(
            "placeholder",
            extraction.GetProperty("summary").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task<long> GivenExtracted(string fixture)
    {
        var recorded = await _mcp.Resolve<RecordPurchase>().Execute(new RecordPurchaseCommand(
            Occurred.AddMinutes(Interlocked.Increment(ref _sequence)),
            59.65m,
            [new ExpenseCommand("Receipt", 59.65m)]));

        await _mcp.Resolve<AttachReceiptImage>().Execute(
            recorded.Purchase.Id,
            DecoderRegressionTests.Photograph(fixture).Content);

        await _mcp.Resolve<RunExtraction>().Execute(recorded.Purchase.Id);

        return recorded.Purchase.Id;
    }

    private async Task<JsonElement> Call(long purchaseId)
    {
        var response = await _mcp.Client.CallToolAsync(
            "get_extraction",
            new Dictionary<string, object?> { ["purchaseId"] = purchaseId });

        var content = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(response.Content[0]);

        return JsonDocument.Parse(content.Text).RootElement.Clone();
    }
}
