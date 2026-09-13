using System.Net;
using System.Text.Json;
using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Tests.Fakes;

namespace Expenses.Desktop.Core.Tests.Ledger;

/// <summary>
/// The confirmation call behind desktop-client's "Confirming a capture creates the purchase" and "A
/// rejected confirmation is reported in place". What is asserted is the JSON on the wire, because
/// that is the contract the API binds, not the client's records.
/// </summary>
public sealed class LedgerClientRecordPurchaseTests
{
    private const string Recorded = """
        {
          "id": 41, "occurredAt": "2026-09-12T18:05:11", "amount": 2.40, "merchantId": 1, "merchantRaw": "Idea",
          "hasReceipt": true, "expenses": [], "totalSaving": 0, "savingPercentage": null,
          "extractionState": "Extracted", "alreadyRecorded": false, "merchant": null, "merchantNewlyAdded": false
        }
        """;

    private readonly FakeHttpMessageHandler _api = new();

    [Fact]
    public async Task The_request_carries_the_capture_echo_and_the_users_values()
    {
        _api.Respond("POST", "/purchases", HttpStatusCode.Created, Recorded);
        var request = new RecordPurchaseRequest(
            2.40m,
            [new ExpenseRequest("Milk", 2.40m, 2m, null, 1.20m, "food", "DAIRY", null, null, null)],
            new MerchantRequest("Idea", null),
            new DateTime(2026, 9, 12, 21, 30, 0),
            new CapturedReceiptRequest("tmp-1", ExtractionState.NeedsReview, null, null, FiscalSource.DecodedFromCode, "https://efi.example/verify"));

        await Subject().RecordPurchase(request, Token);

        var sent = Assert.Single(_api.To("POST", "/purchases"));
        Assert.StartsWith("application/json", sent.ContentType);

        using var json = JsonDocument.Parse(sent.BodyText);
        var root = json.RootElement;
        Assert.Equal(2.40m, root.GetProperty("amount").GetDecimal());

        // Wall-clock time, with no offset and no conversion to UTC.
        Assert.Equal("2026-09-12T21:30:00", root.GetProperty("occurredAt").GetString());

        var line = Assert.Single(root.GetProperty("expenses").EnumerateArray());
        Assert.Equal("Milk", line.GetProperty("description").GetString());
        Assert.Equal("food", line.GetProperty("categoryCode").GetString());
        Assert.Equal("DAIRY", line.GetProperty("categoryRaw").GetString());

        Assert.Equal("Idea", root.GetProperty("merchant").GetProperty("text").GetString());

        var capture = root.GetProperty("capture");
        Assert.Equal("tmp-1", capture.GetProperty("tempKey").GetString());
        Assert.Equal("NeedsReview", capture.GetProperty("state").GetString());
        Assert.Equal("DecodedFromCode", capture.GetProperty("fiscalSource").GetString());
        Assert.Equal("https://efi.example/verify", capture.GetProperty("fiscalPayload").GetString());
    }

    [Fact]
    public async Task A_date_left_to_the_receipt_is_not_sent()
    {
        _api.Respond("POST", "/purchases", HttpStatusCode.Created, Recorded);

        await Subject().RecordPurchase(
            new RecordPurchaseRequest(2.40m, [], null, null, new CapturedReceiptRequest("tmp-1", ExtractionState.Extracted, null, null, FiscalSource.DecodedFromCode, "payload")),
            Token);

        using var json = JsonDocument.Parse(Assert.Single(_api.Requests).BodyText);
        Assert.False(
            json.RootElement.TryGetProperty("occurredAt", out var occurredAt) && occurredAt.ValueKind != JsonValueKind.Null,
            "an unset date must reach the API as absent, so that it takes the receipt's");
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, false)]
    [InlineData(HttpStatusCode.OK, true)]
    public async Task Both_success_outcomes_are_read(HttpStatusCode status, bool alreadyRecorded)
    {
        _api.Respond("POST", "/purchases", status, Recorded.Replace("\"alreadyRecorded\": false", $"\"alreadyRecorded\": {alreadyRecorded.ToString().ToLowerInvariant()}", StringComparison.Ordinal));

        var response = await Subject().RecordPurchase(new RecordPurchaseRequest(2.40m, [], null, null, null), Token);

        Assert.Equal(41, response.Id);
        Assert.Equal(alreadyRecorded, response.AlreadyRecorded);
    }

    [Fact]
    public async Task A_rejected_confirmation_carries_the_ledgers_reason()
    {
        _api.Respond("POST", "/purchases", HttpStatusCode.UnprocessableEntity, """
            { "code": "purchase.amount_mismatch", "message": "The expense lines add up to 2.00, not 2.40.", "fields": {}, "correlationId": null }
            """);

        var failure = await Assert.ThrowsAsync<LedgerException>(
            () => Subject().RecordPurchase(new RecordPurchaseRequest(2.40m, [], null, null, null), Token));

        Assert.Equal("The expense lines add up to 2.00, not 2.40.", failure.Message);
        Assert.Equal("purchase.amount_mismatch", failure.Code);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private LedgerClient Subject()
    {
        return new LedgerClient(FakeHttpMessageHandler.ClientFor(_api));
    }
}
