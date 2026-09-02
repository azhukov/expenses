using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Expenses.Integration.Tests.Harness;

namespace Expenses.Integration.Tests.Ledger;

/// <summary>
/// The whole receipt path through the HTTP adapter — capture, confirmation and a re-run — and the
/// cascade branches end to end (D20). Every part of this is tested in isolation elsewhere; what
/// this adds is that they compose.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReceiptJourneyTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTime Occurred = new(2038, 8, 9, 17, 5, 0, DateTimeKind.Unspecified);

    private static int _sequence;

    public Task InitializeAsync() => postgres.Migrate();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_full_receipt_path_through_the_http_adapter()
    {
        await using var api = new ExpensesApi(postgres.ConnectionString);
        using var client = api.CreateClient();

        // Captured with no purchase behind it: extraction runs synchronously, in this same request.
        var captured = await Capture(client);
        var tempKey = captured.GetProperty("tempKey").GetString();
        var state = captured.GetProperty("state").GetString();

        // Confirmed with the caller's own lines — capture's candidates are never held server-side,
        // so what is asserted here need not match what extraction proposed.
        var recorded = await client.PostAsJsonAsync("/purchases", new
        {
            occurredAt = Next(),
            amount = 20.00m,
            expenses = new[]
            {
                new { description = "Corrected line one", amount = 12.00m },
                new { description = "Corrected line two", amount = 8.00m },
            },
            capture = new { tempKey, state },
        });

        Assert.Equal(HttpStatusCode.Created, recorded.StatusCode);
        var purchase = await ExpensesApi.Read<JsonElement>(recorded);
        var purchaseId = purchase.GetProperty("id").GetInt64();
        Assert.True(purchase.GetProperty("hasReceipt").GetBoolean());

        var extraction = await ExpensesApi.Read<JsonElement>(
            await client.GetAsync($"/purchases/{purchaseId}/extraction"));

        // Already terminal from the moment the purchase exists — no separate extraction step ran.
        Assert.Equal(state, extraction.GetProperty("receipt").GetProperty("state").GetString());
        Assert.False(extraction.GetProperty("candidatesHeld").GetBoolean());

        // Re-running replaces the suggestion, synchronously, and leaves the confirmed expenses alone.
        var reran = await ExpensesApi.Read<JsonElement>(
            await client.PostAsync($"/purchases/{purchaseId}/extraction/rerun", null));
        Assert.NotEmpty(reran.GetProperty("result").GetProperty("candidates").EnumerateArray());

        var afterRerun = await ExpensesApi.Read<JsonElement>(await client.GetAsync($"/purchases/{purchaseId}"));
        Assert.Equal(
            ["Corrected line one", "Corrected line two"],
            afterRerun.GetProperty("expenses").EnumerateArray()
                .Select(expense => expense.GetProperty("description").GetString()));
    }

    [Fact]
    public async Task The_cascade_end_to_end_when_validation_passes()
    {
        var captured = await Capture(("Extraction:Placeholder:Outcome", "Reconciling"));

        Assert.Equal("Extracted", captured.GetProperty("state").GetString());
        Assert.DoesNotContain(
            "vision-expensive",
            captured.GetProperty("result").GetProperty("stagesRun").EnumerateArray()
                .Select(stage => stage.GetString()));
    }

    [Fact]
    public async Task The_cascade_end_to_end_when_validation_fails_and_the_fallback_runs()
    {
        var captured = await Capture(("Extraction:Placeholder:Outcome", "NonReconciling"));

        // Both tiers are the same placeholder here, so both fail; the result kept is the one that
        // failed fewer checks, and the image is put in front of a human (D20).
        Assert.Equal("NeedsReview", captured.GetProperty("state").GetString());
        Assert.Contains(
            "vision-expensive",
            captured.GetProperty("result").GetProperty("stagesRun").EnumerateArray()
                .Select(stage => stage.GetString()));

        var failed = captured.GetProperty("validation").GetProperty("checks").EnumerateArray()
            .Where(check => check.GetProperty("outcome").GetString() == "Failed")
            .ToList();

        Assert.NotEmpty(failed);
    }

    [Fact]
    public async Task The_cascade_end_to_end_when_no_stage_produces_anything()
    {
        var captured = await Capture(("Extraction:Placeholder:Outcome", "Failure"));

        Assert.Equal("Failed", captured.GetProperty("state").GetString());
        Assert.False(string.IsNullOrWhiteSpace(captured.GetProperty("failureReason").GetString()));

        // Nothing produced a result, so the caller can still confirm with lines entered by hand.
        Assert.Equal(JsonValueKind.Null, captured.GetProperty("result").ValueKind);
    }

    private async Task<JsonElement> Capture(params (string Key, string Value)[] settings)
    {
        await using var api = new ExpensesApi(postgres.ConnectionString, settings);
        using var client = api.CreateClient();

        return await Capture(client);
    }

    private static async Task<JsonElement> Capture(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Jpeg());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "receipt.jpg");

        var response = await client.PostAsync("/receipts/capture", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ExpensesApi.Read<JsonElement>(response);
    }

    /// <summary>Distinct bytes per call, so each journey owns its stored image.</summary>
    private static byte[] Jpeg() =>
        [0xFF, 0xD8, 0xFF, 0xE0, (byte)_sequence, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x05, (byte)(_sequence >> 8)];

    private static DateTime Next() => Occurred.AddMinutes(Interlocked.Increment(ref _sequence));
}
