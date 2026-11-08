using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Expenses.Application.Interfaces;
using Expenses.Integration.Tests.Harness;

namespace Expenses.Integration.Tests.Api;

/// <summary>
/// Scenarios from add-clear-all-expenses, api-surface: "Purchases can be counted over both
/// interfaces" ("Counting over HTTP", "Counting over MCP"); "The ledger can be cleared over HTTP"
/// ("Clearing over HTTP", "A clear without an expected number over HTTP", "A stale clear over HTTP",
/// "Clearing from an allowed browser origin"). And end to end through the host, receipt-ingestion's
/// "Receipt files go with the cleared purchases" and "Confirming a capture taken before a clear".
///
/// Each test runs against a database of its own, because a clear of the shared suite database would
/// delete what every other class arranged.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LedgerClearTests(PostgresFixture postgres)
{
    private const string Allowed = "http://client.test:5173";

    private static readonly DateTime s_occurred = new(2040, 2, 11, 8, 30, 0, DateTimeKind.Unspecified);

    private static int s_sequence;

    [Fact]
    public async Task Counting_over_http()
    {
        await using var api = await Api("clear_http_count");
        using var client = api.CreateClient();
        await GivenPurchases(client, 3);

        var response = await client.GetAsync("/purchases/count");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, (await ExpensesApi.Read<JsonElement>(response)).GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Counting_over_mcp_matches_http()
    {
        string connectionString = await postgres.ProvisionAnother("clear_mcp_count");
        await using var api = new ExpensesApi(connectionString);
        using var client = api.CreateClient();
        await GivenPurchases(client, 2);
        await using var mcp = await ExpensesMcp.Start(connectionString);

        var result = await mcp.Client.CallToolAsync("count_purchases", new Dictionary<string, object?>());

        Assert.True(result.IsError is not true, "Counting purchases should succeed.");
        var content = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(result.Content[0]);
        int overMcp = JsonDocument.Parse(content.Text).RootElement.GetProperty("count").GetInt32();
        int overHttp = (await ExpensesApi.Read<JsonElement>(await client.GetAsync("/purchases/count")))
            .GetProperty("count").GetInt32();

        Assert.Equal(2, overMcp);
        Assert.Equal(overHttp, overMcp);
    }

    [Fact]
    public async Task Clearing_over_http()
    {
        await using var api = await Api("clear_http_clear");
        using var client = api.CreateClient();
        long first = (await GivenPurchases(client, 3))[0];

        var response = await client.DeleteAsync("/purchases?expectedCount=3");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, (await ExpensesApi.Read<JsonElement>(response)).GetProperty("deleted").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/purchases/{first}")).StatusCode);
        Assert.Empty((await ExpensesApi.Read<JsonElement>(await client.GetAsync("/purchases"))).EnumerateArray());
    }

    [Fact]
    public async Task A_clear_without_an_expected_number_over_http()
    {
        await using var api = await Api("clear_http_missing");
        using var client = api.CreateClient();
        await GivenPurchases(client, 1);

        var response = await client.DeleteAsync("/purchases");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ExpensesApi.Read<JsonElement>(response);
        Assert.Equal("purchase.clear.expected_count_required", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
        Assert.True(error.GetProperty("fields").TryGetProperty("expectedCount", out _));
        Assert.Equal(1, await Count(client));
    }

    [Fact]
    public async Task A_stale_clear_over_http()
    {
        await using var api = await Api("clear_http_stale");
        using var client = api.CreateClient();
        await GivenPurchases(client, 13);

        var response = await client.DeleteAsync("/purchases?expectedCount=12");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await ExpensesApi.Read<JsonElement>(response);
        Assert.Equal("purchase.clear.count_mismatch", error.GetProperty("code").GetString());
        Assert.Contains("13", error.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal(13, await Count(client));
    }

    [Fact]
    public async Task Clearing_from_an_allowed_browser_origin()
    {
        string connectionString = await postgres.ProvisionAnother("clear_http_cors");
        await using var api = new ExpensesApi(
            connectionString,
            ("Cors:AllowedOrigins:0", Allowed),
            ("Database:ApplyMigrationsOnStartup", "true"))
        {
            Environment = "Testing",
        };
        using var client = api.CreateClient();

        var preflight = new HttpRequestMessage(HttpMethod.Options, "/purchases?expectedCount=0");
        preflight.Headers.Add("Origin", Allowed);
        preflight.Headers.Add("Access-Control-Request-Method", "DELETE");
        var answered = await client.SendAsync(preflight);

        Assert.True(answered.IsSuccessStatusCode);
        Assert.Contains("DELETE", Header(answered, "Access-Control-Allow-Methods"), StringComparison.Ordinal);

        var clear = new HttpRequestMessage(HttpMethod.Delete, "/purchases?expectedCount=0");
        clear.Headers.Add("Origin", Allowed);
        var response = await client.SendAsync(clear);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Allowed, Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Receipt_files_go_and_pending_captures_stay()
    {
        await using var api = await Api("clear_http_journey");
        using var client = api.CreateClient();

        var first = await Capture(client, Jpeg(0xC1));
        long purchaseId = await Confirm(client, first, 6.60m);
        string storageKey = (await api.Resolve<IPurchaseRepository>().FindById(purchaseId))!.Receipt!.StorageKey;
        Assert.NotNull(await api.Resolve<IReceiptImageStore>().Read(storageKey));

        var pending = await Capture(client, Jpeg(0xC2));

        var cleared = await client.DeleteAsync("/purchases?expectedCount=1");
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);

        Assert.Null(await api.Resolve<IReceiptImageStore>().Read(storageKey));

        long confirmedAfter = await Confirm(client, pending, 7.70m);
        var purchase = await ExpensesApi.Read<JsonElement>(await client.GetAsync($"/purchases/{confirmedAfter}"));
        Assert.True(purchase.GetProperty("hasReceiptImage").GetBoolean());
    }

    private async Task<ExpensesApi> Api(string database)
        => new(await postgres.ProvisionAnother(database), ("Extraction:Vision:Engine", "placeholder"));

    private static async Task<List<long>> GivenPurchases(HttpClient client, int count)
    {
        List<long> ids = [];

        for (int index = 0; index < count; index++)
        {
            var response = await client.PostAsJsonAsync("/purchases", new
            {
                occurredAt = Next(),
                amount = 1.00m + index,
                expenses = new[] { new { description = "Line", amount = 1.00m + index, unitCode = "PCS" } },
            });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            ids.Add((await ExpensesApi.Read<JsonElement>(response)).GetProperty("id").GetInt64());
        }

        return ids;
    }

    private static async Task<int> Count(HttpClient client)
        => (await ExpensesApi.Read<JsonElement>(await client.GetAsync("/purchases/count"))).GetProperty("count").GetInt32();

    private static async Task<JsonElement> Capture(HttpClient client, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "receipt.jpg");

        var response = await client.PostAsync("/receipts/capture", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ExpensesApi.Read<JsonElement>(response);
    }

    private static async Task<long> Confirm(HttpClient client, JsonElement captured, decimal amount)
    {
        var response = await client.PostAsJsonAsync("/purchases", new
        {
            occurredAt = Next(),
            amount,
            expenses = new[] { new { description = "Line", amount, unitCode = "PCS" } },
            capture = new
            {
                tempKey = captured.GetProperty("tempKey").GetString(),
                state = captured.GetProperty("state").GetString(),
            },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await ExpensesApi.Read<JsonElement>(response)).GetProperty("id").GetInt64();
    }

    private static string Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : string.Empty;

    private static DateTime Next() => s_occurred.AddMinutes(Interlocked.Increment(ref s_sequence));

    private static byte[] Jpeg(byte seed)
        => [0xFF, 0xD8, 0xFF, 0xE0, seed, 0x4A, 0x46, 0x49, 0x46, 0x00, seed, 0x03];
}
