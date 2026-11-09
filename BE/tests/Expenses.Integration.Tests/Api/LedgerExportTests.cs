using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Expenses.Integration.Tests.Harness;

namespace Expenses.Integration.Tests.Api;

/// <summary>
/// Scenarios from add-csv-export, api-surface: "The ledger can be exported over HTTP" ("Exporting
/// everything over HTTP", "Exporting a range over HTTP", "An open-ended range is named for its open
/// end", "An inverted range over HTTP", "A malformed date over HTTP", "Exporting from an allowed
/// browser origin"), and ledger-export's "Exporting changes nothing" through the host.
///
/// Each test that reads rows runs against a database of its own, so "everything" means only what
/// the test arranged.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LedgerExportTests(PostgresFixture postgres)
{
    private const string Allowed = "http://client.test:5173";

    private const string Header =
        "purchase_id,occurred_at,merchant,description,quantity,unit,unit_price,list_unit_price,discount_amount,category,amount";

    [Fact]
    public async Task Exporting_everything_over_http()
    {
        await using var api = await Api("export_http_all");
        using var client = api.CreateClient();
        long september = await GivenPurchase(client, new DateTime(2026, 9, 30, 18, 0, 0), "Bread, white", 1.20m);
        long october = await GivenPurchase(client, new DateTime(2026, 10, 14, 13, 5, 0), "Čokolada", 2.50m);

        var response = await client.GetAsync("/purchases/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("expenses-all.csv", FileName(response));

        string[] rows = await Rows(response);
        Assert.Equal(
            [
                Header,
                $"{september},2026-09-30 18:00:00,,\"Bread, white\",1,pcs,,,,,1.20",
                $"{october},2026-10-14 13:05:00,,Čokolada,1,pcs,,,,,2.50",
            ],
            rows);
    }

    [Fact]
    public async Task Exporting_a_range_over_http()
    {
        await using var api = await Api("export_http_range");
        using var client = api.CreateClient();
        await GivenPurchase(client, new DateTime(2026, 9, 30, 23, 59, 0), "September", 1.00m);
        await GivenPurchase(client, new DateTime(2026, 10, 31, 23, 59, 0), "October", 2.00m);
        await GivenPurchase(client, new DateTime(2026, 11, 1, 0, 0, 0), "November", 3.00m);

        var response = await client.GetAsync("/purchases/export?from=2026-10-01&to=2026-10-31");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("expenses-2026-10-01-to-2026-10-31.csv", FileName(response));
        string[] rows = await Rows(response);
        Assert.Equal(2, rows.Length);
        Assert.Contains(",October,", rows[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("?from=2026-10-15", "expenses-2026-10-15-to-latest.csv")]
    [InlineData("?to=2026-10-31", "expenses-earliest-to-2026-10-31.csv")]
    public async Task An_open_ended_range_is_named_for_its_open_end(string query, string fileName)
    {
        await using var api = new ExpensesApi(postgres.ConnectionString);
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/purchases/export{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(fileName, FileName(response));
    }

    [Fact]
    public async Task An_inverted_range_over_http()
    {
        await using var api = new ExpensesApi(postgres.ConnectionString);
        using var client = api.CreateClient();

        var response = await client.GetAsync("/purchases/export?from=2026-10-31&to=2026-10-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var error = await ExpensesApi.Read<JsonElement>(response);
        Assert.Equal("listing.range_invalid", error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("fields").TryGetProperty("from", out _));
        Assert.True(error.GetProperty("fields").TryGetProperty("to", out _));
    }

    [Fact]
    public async Task A_malformed_date_over_http()
    {
        await using var api = new ExpensesApi(postgres.ConnectionString);
        using var client = api.CreateClient();

        var response = await client.GetAsync("/purchases/export?from=yesterday");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var error = await ExpensesApi.Read<JsonElement>(response);
        Assert.Equal("request_invalid", error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("fields").TryGetProperty("from", out _));
    }

    [Fact]
    public async Task Exporting_from_an_allowed_browser_origin()
    {
        await using var api = new ExpensesApi(
            postgres.ConnectionString,
            ("Cors:AllowedOrigins:0", Allowed))
        {
            Environment = "Testing",
        };
        using var client = api.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/purchases/export?from=2026-10-01&to=2026-10-31");
        request.Headers.Add("Origin", Allowed);
        request.Headers.Add("Accept", "text/csv");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            Allowed,
            response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? string.Join(",", values) : null);
    }

    [Fact]
    public async Task Exporting_changes_nothing()
    {
        await using var api = await Api("export_http_read_only");
        using var client = api.CreateClient();
        await GivenPurchase(client, new DateTime(2026, 10, 1, 9, 0, 0), "Line", 1.00m);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/purchases/export")).StatusCode);

        var count = await ExpensesApi.Read<JsonElement>(await client.GetAsync("/purchases/count"));
        Assert.Equal(1, count.GetProperty("count").GetInt32());
    }

    private async Task<ExpensesApi> Api(string database)
        => new(await postgres.ProvisionAnother(database), ("Extraction:Vision:Engine", "placeholder"));

    private static async Task<long> GivenPurchase(HttpClient client, DateTime occurredAt, string description, decimal amount)
    {
        var response = await client.PostAsJsonAsync("/purchases", new
        {
            occurredAt = occurredAt.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            amount,
            expenses = new[] { new { description, amount, unitCode = "PCS" } },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await ExpensesApi.Read<JsonElement>(response)).GetProperty("id").GetInt64();
    }

    private static string? FileName(HttpResponseMessage response)
        => response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

    /// <summary>The rows of the body, after checking it opens with the UTF-8 byte-order mark.</summary>
    private static async Task<string[]> Rows(HttpResponseMessage response)
    {
        byte[] body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal([0xEF, 0xBB, 0xBF], body[..3]);

        return new UTF8Encoding(false).GetString(body, 3, body.Length - 3)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }
}
