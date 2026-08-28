using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Expenses.Application.Receipts;
using Expenses.Integration.Tests.Harness;

namespace Expenses.Integration.Tests.Ledger;

/// <summary>
/// The whole receipt path through the HTTP adapter — upload, extraction, candidates, confirmation
/// and a re-run — and the cascade branches end to end (D20). Every part of this is tested in
/// isolation elsewhere; what this adds is that they compose.
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

        var purchase = await ExpensesApi.Read<JsonElement>(await client.PostAsJsonAsync("/purchases", new
        {
            occurredAt = Next(),
            amount = 20.00m,
            expenses = new[] { new { description = "Whole receipt", amount = 20.00m } },
        }));

        var purchaseId = purchase.GetProperty("id").GetInt64();

        // Upload, which stores the image and queues extraction without waiting for it.
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Jpeg());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "receipt.jpg");

        var uploaded = await client.PostAsync($"/purchases/{purchaseId}/receipt", form);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);

        // The suite drives the cascade itself; in a running host the drain does exactly this.
        await api.Resolve<RunExtraction>().Execute(purchaseId);

        var extraction = await ExpensesApi.Read<JsonElement>(
            await client.GetAsync($"/purchases/{purchaseId}/extraction"));

        Assert.Equal("Extracted", extraction.GetProperty("receipt").GetProperty("state").GetString());
        Assert.NotEmpty(extraction.GetProperty("result").GetProperty("candidates").EnumerateArray());

        // Confirming corrected lines, since the placeholder's numbers are its own.
        var confirmed = await client.PostAsJsonAsync($"/purchases/{purchaseId}/extraction/confirm", new
        {
            expenses = new[]
            {
                new { description = "Corrected line one", amount = 12.00m },
                new { description = "Corrected line two", amount = 8.00m },
            },
        });

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var confirmedPurchase = await ExpensesApi.Read<JsonElement>(confirmed);
        Assert.Equal(2, confirmedPurchase.GetProperty("expenses").GetArrayLength());

        // Re-running replaces the suggestion and leaves the confirmed expenses alone.
        var reran = await client.PostAsync($"/purchases/{purchaseId}/extraction/rerun", null);
        Assert.Equal(HttpStatusCode.Accepted, reran.StatusCode);
        await api.Resolve<RunExtraction>().Execute(purchaseId);

        var afterRerun = await ExpensesApi.Read<JsonElement>(await client.GetAsync($"/purchases/{purchaseId}"));
        Assert.Equal(
            ["Corrected line one", "Corrected line two"],
            afterRerun.GetProperty("expenses").EnumerateArray()
                .Select(expense => expense.GetProperty("description").GetString()));
    }

    [Fact]
    public async Task The_cascade_end_to_end_when_validation_passes()
    {
        var extraction = await Journey(("Extraction:Placeholder:Outcome", "Reconciling"));

        Assert.Equal("Extracted", extraction.GetProperty("receipt").GetProperty("state").GetString());
        Assert.DoesNotContain(
            "vision-expensive",
            extraction.GetProperty("result").GetProperty("stagesRun").EnumerateArray()
                .Select(stage => stage.GetString()));
    }

    [Fact]
    public async Task The_cascade_end_to_end_when_validation_fails_and_the_fallback_runs()
    {
        var extraction = await Journey(("Extraction:Placeholder:Outcome", "NonReconciling"));

        // Both tiers are the same placeholder here, so both fail; the result kept is the one that
        // failed fewer checks, and the image is put in front of a human (D20).
        Assert.Equal("NeedsReview", extraction.GetProperty("receipt").GetProperty("state").GetString());
        Assert.Contains(
            "vision-expensive",
            extraction.GetProperty("result").GetProperty("stagesRun").EnumerateArray()
                .Select(stage => stage.GetString()));

        var failed = extraction.GetProperty("validation").GetProperty("checks").EnumerateArray()
            .Where(check => check.GetProperty("outcome").GetString() == "Failed")
            .ToList();

        Assert.NotEmpty(failed);
    }

    [Fact]
    public async Task The_cascade_end_to_end_when_no_stage_produces_anything()
    {
        var extraction = await Journey(("Extraction:Placeholder:Outcome", "Failure"));

        Assert.Equal("Failed", extraction.GetProperty("receipt").GetProperty("state").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            extraction.GetProperty("receipt").GetProperty("failureReason").GetString()));

        // The purchase and the image survive, so the user can enter the lines by hand.
        Assert.Equal(JsonValueKind.Null, extraction.GetProperty("result").ValueKind);
    }

    private async Task<JsonElement> Journey(params (string Key, string Value)[] settings)
    {
        await using var api = new ExpensesApi(postgres.ConnectionString, settings);
        using var client = api.CreateClient();

        var purchase = await ExpensesApi.Read<JsonElement>(await client.PostAsJsonAsync("/purchases", new
        {
            occurredAt = Next(),
            amount = 15.00m,
            expenses = new[] { new { description = "Cascade", amount = 15.00m } },
        }));

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Jpeg());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "receipt.jpg");

        var purchaseId = purchase.GetProperty("id").GetInt64();
        var uploaded = await client.PostAsync($"/purchases/{purchaseId}/receipt", form);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);

        await api.Resolve<RunExtraction>().Execute(purchaseId);

        return await ExpensesApi.Read<JsonElement>(await client.GetAsync($"/purchases/{purchaseId}/extraction"));
    }

    /// <summary>Distinct bytes per call, so each journey owns its stored image.</summary>
    private static byte[] Jpeg() =>
        [0xFF, 0xD8, 0xFF, 0xE0, (byte)_sequence, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x05, (byte)(_sequence >> 8)];

    private static DateTime Next() => Occurred.AddMinutes(Interlocked.Increment(ref _sequence));
}
