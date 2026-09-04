using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Expenses.Integration.Tests.Harness;

namespace Expenses.Integration.Tests.Ledger;

/// <summary>
/// Scenarios from api-surface: "Both interfaces expose the same behaviour" — the same operation
/// over either interface, the same validation outcome, and a duplicate guard that spans them.
/// Two adapters over one Application layer is the whole design (D1); only running both proves it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CrossAdapterTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTime s_occurred = new(2037, 6, 7, 13, 20, 0, DateTimeKind.Unspecified);

    private static int s_sequence;

    private ExpensesApi _api = null!;
    private HttpClient _http = null!;
    private ExpensesMcp _mcp = null!;

    public async Task InitializeAsync()
    {
        await postgres.Migrate();
        _api = new ExpensesApi(postgres.ConnectionString);
        _http = _api.CreateClient();
        _mcp = await ExpensesMcp.Start(postgres.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _api.DisposeAsync();
        await _mcp.DisposeAsync();
    }

    [Fact]
    public async Task The_same_operation_over_either_interface_produces_the_same_purchase()
    {
        var overHttp = await ExpensesApi.Read<JsonElement>(await _http.PostAsJsonAsync("/purchases", new
        {
            occurredAt = Next(),
            amount = 8.48m,
            merchant = new { text = "AROMA", taxId = "09940001" },
            expenses = new[]
            {
                new { description = "Sladoled", amount = 4.49m },
                new { description = "Cokolada", amount = 3.99m },
            },
        }));

        var overMcp = await CallMcp("record_purchase", new Dictionary<string, object?>
        {
            ["occurredAt"] = Next(),
            ["amount"] = 8.48m,
            ["merchant"] = new { text = "AROMA", taxId = "09940001" },
            ["expenses"] = new[]
            {
                new { description = "Sladoled", amount = 4.49m },
                new { description = "Cokolada", amount = 3.99m },
            },
        });

        var mcpPurchase = overMcp.GetProperty("purchase");

        Assert.Equal(overHttp.GetProperty("amount").GetDecimal(), mcpPurchase.GetProperty("amount").GetDecimal());
        Assert.Equal(
            overHttp.GetProperty("merchantId").GetInt64(),
            mcpPurchase.GetProperty("merchantId").GetInt64());
        Assert.Equal(
            overHttp.GetProperty("expenses").GetArrayLength(),
            mcpPurchase.GetProperty("expenses").GetArrayLength());

        // The same purchase read back over the other interface is the same purchase.
        var readBack = await CallMcp("get_purchase", new Dictionary<string, object?>
        {
            ["id"] = overHttp.GetProperty("id").GetInt64(),
        });

        Assert.Equal(8.48m, readBack.GetProperty("amount").GetDecimal());
        Assert.Equal("AROMA", readBack.GetProperty("merchantRaw").GetString());
    }

    [Fact]
    public async Task The_same_validation_outcome_over_either_interface()
    {
        var occurred = Next();
        var lines = new[]
        {
            new { description = "Shoes", amount = 60.00m },
            new { description = "Socks", amount = 18.50m },
        };

        var overHttp = await _http.PostAsJsonAsync("/purchases", new { occurredAt = occurred, amount = 80.00m, expenses = lines });
        var httpError = await ExpensesApi.Read<JsonElement>(overHttp);

        var overMcp = await _mcp.Client.CallToolAsync("record_purchase", new Dictionary<string, object?>
        {
            ["occurredAt"] = occurred,
            ["amount"] = 80.00m,
            ["expenses"] = lines,
        });

        string mcpMessage = string.Join(
            " ",
            overMcp.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(block => block.Text));

        Assert.Equal(HttpStatusCode.BadRequest, overHttp.StatusCode);
        Assert.True(overMcp.IsError);

        // Both report the same discrepancy, because both are relaying one rule from one place (D1).
        Assert.Equal("purchase.reconciliation_mismatch", httpError.GetProperty("code").GetString());
        Assert.Contains("purchase.reconciliation_mismatch", mcpMessage, StringComparison.Ordinal);
        Assert.Contains("78.50", httpError.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Contains("78.50", mcpMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_duplicate_guard_spans_interfaces()
    {
        var occurred = Next();

        var overMcp = await CallMcp("record_purchase", new Dictionary<string, object?>
        {
            ["occurredAt"] = occurred,
            ["amount"] = 4.44m,
            ["expenses"] = new[] { new { description = "Tea", amount = 4.44m } },
        });

        var overHttp = await _http.PostAsJsonAsync("/purchases", new
        {
            occurredAt = occurred,
            amount = 4.44m,
            expenses = new[] { new { description = "Tea", amount = 4.44m } },
        });

        var httpPurchase = await ExpensesApi.Read<JsonElement>(overHttp);

        // The guard is a property of the ledger, not of a front door (D4).
        Assert.Equal(HttpStatusCode.OK, overHttp.StatusCode);
        Assert.True(httpPurchase.GetProperty("alreadyRecorded").GetBoolean());
        Assert.Equal(
            overMcp.GetProperty("purchase").GetProperty("id").GetInt64(),
            httpPurchase.GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task Reference_data_reads_the_same_over_either_interface()
    {
        var overHttp = await ExpensesApi.Read<JsonElement>(await _http.GetAsync("/units"));
        var overMcp = await CallMcp("list_units", new Dictionary<string, object?>());

        Assert.Equal(
            overHttp.EnumerateArray().Select(unit => unit.GetProperty("code").GetString()),
            overMcp.EnumerateArray().Select(unit => unit.GetProperty("code").GetString()));
    }

    private async Task<JsonElement> CallMcp(string tool, IReadOnlyDictionary<string, object?> arguments)
    {
        var result = await _mcp.Client.CallToolAsync(tool, arguments);
        var content = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(result.Content[0]);

        return JsonDocument.Parse(content.Text).RootElement.Clone();
    }

    private static DateTime Next() => s_occurred.AddMinutes(Interlocked.Increment(ref s_sequence));
}
