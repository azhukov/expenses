using System.Text.Json;
using Expenses.Application.Dtos;
using Expenses.Application.Services;
using Expenses.Integration.Tests.Harness;

namespace Expenses.Integration.Tests.Mcp;

/// <summary>
/// Scenarios from api-surface: "MCP tool surface", "A repeated tool call is not an error", "Receipt
/// upload is HTTP-only", "Merchants are addressed unambiguously over MCP", "Extraction results are
/// legible over both interfaces", "Errors carry enough detail to be acted on".
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class McpAdapterTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTime s_occurred = new(2035, 2, 3, 8, 15, 0, DateTimeKind.Unspecified);

    private static readonly string[] s_terminalStates = ["Extracted", "NeedsReview", "Failed"];

    private static int s_sequence;

    private ExpensesMcp _mcp = null!;

    public async Task InitializeAsync()
    {
        await postgres.Migrate();
        // The placeholder path being reached is what these scenarios are about, not the real vision
        // engine's own behaviour (D33).
        _mcp = await ExpensesMcp.Start(postgres.ConnectionString, ("Extraction:Vision:Engine", "placeholder"));
    }

    public ValueTask DisposeAsync() => _mcp.DisposeAsync();

    Task IAsyncLifetime.DisposeAsync() => _mcp.DisposeAsync().AsTask();

    [Fact]
    public async Task Tools_are_discoverable()
    {
        var tools = await _mcp.Client.ListToolsAsync();
        var names = tools.Select(tool => tool.Name).ToList();

        Assert.Contains("record_purchase", names);
        Assert.Contains("get_purchase", names);
        Assert.Contains("list_purchases", names);
        Assert.Contains("list_categories", names);
        Assert.Contains("list_units", names);
        Assert.Contains("list_merchants", names);
        Assert.Contains("search_merchants", names);
        Assert.Contains("get_extraction", names);
        Assert.Contains("rerun_extraction", names);
        Assert.Contains("confirm_capture", names);

        // Each tool says what it does and when to use it, and declares its arguments.
        Assert.All(tools, tool =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
            Assert.Equal(JsonValueKind.Object, tool.JsonSchema.ValueKind);
            Assert.True(tool.JsonSchema.TryGetProperty("properties", out _));
        });
    }

    [Fact]
    public async Task Image_bytes_are_never_accepted_as_a_tool_argument()
    {
        var tools = await _mcp.Client.ListToolsAsync();

        // Uploading is HTTP-only; MCP references images that are already stored. A tool that took
        // bytes would be a second, worse upload path.
        foreach (var tool in tools)
        {
            if (!tool.JsonSchema.TryGetProperty("properties", out var properties))
            {
                continue;
            }

            foreach (var argument in properties.EnumerateObject())
            {
                Assert.DoesNotContain("byte", argument.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("content", argument.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("file", argument.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task Recording_a_purchase_returns_the_stored_purchase_and_identifier()
    {
        var result = await Record(Next(), 12.40m, "Lunch");

        Assert.False(result.GetProperty("alreadyRecorded").GetBoolean());
        Assert.True(result.GetProperty("purchase").GetProperty("id").GetInt64() > 0);
        Assert.Equal(12.40m, result.GetProperty("purchase").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Assistant_repeats_a_call()
    {
        var occurred = Next();

        var first = await Record(occurred, 6.60m, "Coffee");
        var second = await Record(occurred, 6.60m, "Coffee");

        // A success that says what happened, rather than an error the assistant would route
        // around by nudging the amount or apologising to the user (D3).
        Assert.False(first.GetProperty("alreadyRecorded").GetBoolean());
        Assert.True(second.GetProperty("alreadyRecorded").GetBoolean());
        Assert.Contains("already", second.GetProperty("summary").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            first.GetProperty("purchase").GetProperty("id").GetInt64(),
            second.GetProperty("purchase").GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task Genuine_failures_are_still_errors()
    {
        string error = await Failing("record_purchase", new Dictionary<string, object?>
        {
            ["occurredAt"] = Next(),
            ["amount"] = 80.00m,
            ["expenses"] = new[]
            {
                new { description = "Shoes", amount = 60.00m, unitCode = "PCS" },
                new { description = "Socks", amount = 18.50m, unitCode = "PCS" },
            },
        });

        // Reported as a tool error the assistant can relay, carrying the discrepancy and the
        // stable code — the same failure the HTTP interface reports (D1).
        Assert.Contains("78.50", error, StringComparison.Ordinal);
        Assert.Contains("purchase.reconciliation_mismatch", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reference_data_is_resolved_by_code_and_a_display_name_is_rejected()
    {
        var categories = await Call("list_categories", new Dictionary<string, object?>());
        var codes = categories.EnumerateArray().Select(category => category.GetProperty("code").GetString()).ToList();

        Assert.Contains("GROCERIES", codes);

        var byCode = await Record(Next(), 5.00m, "Bread", categoryCode: "GROCERIES");
        Assert.Equal(
            5.00m,
            byCode.GetProperty("purchase").GetProperty("expenses")[0].GetProperty("amount").GetDecimal());

        // A display name is not a code, and the ledger says so rather than guessing.
        string error = await Failing("record_purchase", new Dictionary<string, object?>
        {
            ["occurredAt"] = Next(),
            ["amount"] = 5.00m,
            ["expenses"] = new[]
            {
                new { description = "Bread", amount = 5.00m, unitCode = "PCS", categoryCode = "Groceries and things" },
            },
        });

        Assert.Contains("Groceries and things", error, StringComparison.Ordinal);
        Assert.Contains("category.not_found", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_newly_added_merchant_is_reported_as_newly_added()
    {
        var occurred = Next();
        var first = await Call("record_purchase", new Dictionary<string, object?>
        {
            ["occurredAt"] = occurred,
            ["amount"] = 3.30m,
            ["expenses"] = new[] { new { description = "Kafa", amount = 3.30m, unitCode = "PCS" } },
            ["merchant"] = new { text = "Kafe MCP", taxId = "09900001" },
        });

        Assert.True(first.GetProperty("merchantNewlyAdded").GetBoolean());

        var second = await Call("record_purchase", new Dictionary<string, object?>
        {
            ["occurredAt"] = Next(),
            ["amount"] = 3.40m,
            ["expenses"] = new[] { new { description = "Kafa", amount = 3.40m, unitCode = "PCS" } },
            ["merchant"] = new { text = "Kafe MCP", taxId = "09900001" },
        });

        Assert.False(second.GetProperty("merchantNewlyAdded").GetBoolean());

        var found = await Call("search_merchants", new Dictionary<string, object?> { ["term"] = "Kafe MCP" });
        Assert.NotEmpty(found.EnumerateArray());
    }

    [Fact]
    public async Task Arithmetic_checks_and_corroboration_are_readable_through_the_extraction_tools()
    {
        long purchaseId = await GivenExtractedReceipt();

        // A capture's candidates are never held server-side, so confirming it leaves none held;
        // re-running produces them, synchronously, against the now-promoted image.
        await Call("rerun_extraction", new Dictionary<string, object?> { ["purchaseId"] = purchaseId });

        var extraction = await Call("get_extraction", new Dictionary<string, object?>
        {
            ["purchaseId"] = purchaseId,
        });

        Assert.Equal("Extracted", extraction.GetProperty("state").GetString());
        Assert.Equal("placeholder", extraction.GetProperty("engineName").GetString());
        Assert.Contains(
            "vision",
            extraction.GetProperty("stepsRun").EnumerateArray().Select(stage => stage.GetString()));

        var checks = extraction.GetProperty("checks").EnumerateArray().ToList();
        Assert.Contains(checks, check => check.GetProperty("check").GetString() == "line_sum");
        Assert.All(checks, check => Assert.False(string.IsNullOrWhiteSpace(
            check.GetProperty("outcome").GetString())));

        Assert.Equal("Unverified", extraction.GetProperty("fiscalCorroboration").GetString());
    }

    [Fact]
    public async Task An_assistant_can_re_run_extraction_for_a_stored_image()
    {
        long purchaseId = await GivenExtractedReceipt();

        var reran = await Call("rerun_extraction", new Dictionary<string, object?>
        {
            ["purchaseId"] = purchaseId,
        });

        // Synchronous now: the new terminal state and candidates are back in this same response.
        Assert.Contains(reran.GetProperty("state").GetString(), s_terminalStates);
    }

    /// <summary>
    /// api-surface, "Receipt capture is HTTP-only": "MCP confirms a fiscal-only capture"; and
    /// "Confirming a capture is available over both interfaces": "Confirming a fiscal-only capture
    /// over either interface". The capture itself is HTTP-only, so what an HTTP fiscal capture
    /// reports is handed over here as it would be.
    /// </summary>
    [Fact]
    public async Task An_assistant_confirms_a_fiscal_only_capture_by_its_payload()
    {
        const string Payload =
            "https://mapr.tax.gov.me/ic/#/verify?iic=MCP-FISCAL-ONLY-1&tin=02365928&crtd=2026-08-29T14:59:22+02:00";

        var confirmed = await Call("confirm_capture", new Dictionary<string, object?>
        {
            ["capture"] = new
            {
                state = "Extracted",
                jikr = "a1b2c3d4-0000-0000-0000-000000000000",
                fiscalSource = "RetrievedFromService",
                fiscalPayload = Payload,
            },
            ["amount"] = 3.20m,
            ["expenses"] = new[] { new { description = "Coffee", amount = 3.20m, unitCode = "PCS" } },
            ["occurredAt"] = Next(),
        });

        var purchase = confirmed.GetProperty("purchase");
        Assert.False(purchase.GetProperty("hasReceiptImage").GetBoolean());
        Assert.Equal("Extracted", purchase.GetProperty("extractionState").GetString());
        Assert.Equal("MCP-FISCAL-ONLY-1", purchase.GetProperty("fiscal").GetProperty("ikofExtracted").GetString());
        Assert.Equal(Payload, purchase.GetProperty("fiscal").GetProperty("payload").GetString());
    }

    [Fact]
    public async Task A_confirmation_naming_neither_a_key_nor_a_payload_is_rejected_over_mcp()
    {
        string error = await Failing("confirm_capture", new Dictionary<string, object?>
        {
            ["capture"] = new { state = "Extracted" },
            ["amount"] = 1.00m,
            ["expenses"] = new[] { new { description = "Coffee", amount = 1.00m, unitCode = "PCS" } },
            ["occurredAt"] = Next(),
        });

        Assert.Contains("capture.identity_required", error, StringComparison.Ordinal);
    }

    private async Task<long> GivenExtractedReceipt()
    {
        var captured = await _mcp.Resolve<ReceiptService>().Capture(
            [0xFF, 0xD8, 0xFF, 0xE0, (byte)s_sequence, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x04],
            new FiscalIdentifiers("MCP-IKOF-1"),
            "https://mapr.tax.gov.me/ic/#/verify?iic=MCP-IKOF-1");

        // A receipt is addressed by its purchase; there is no image identifier anywhere (D11).
        var recorded = await _mcp.Resolve<PurchaseService>().Record(
            Next(),
            10.00m,
            [new ExpenseCommand("Placeholder", 10.00m, UnitCode: "PCS")],
            capture: new CapturedReceiptCommand(
                captured.TempKey,
                captured.State,
                captured.FailureReason,
                captured.Extracted.Jikr ?? captured.Supplied.Jikr,
                captured.FiscalSource,
                captured.FiscalPayload));

        return recorded.Purchase.Id;
    }

    private ValueTask<JsonElement> Record(
        DateTime occurredAt,
        decimal amount,
        string description,
        string? categoryCode = null)
        => Call("record_purchase", new Dictionary<string, object?>
        {
            ["occurredAt"] = occurredAt,
            ["amount"] = amount,
            ["expenses"] = new[] { new { description, amount, unitCode = "PCS", categoryCode } },
        });

    /// <summary>
    /// Calls a tool expecting it to fail, and returns the text an assistant would read. A failed
    /// tool call is an error result rather than a transport error, which is what lets an assistant
    /// act on it.
    /// </summary>
    private async ValueTask<string> Failing(string tool, IReadOnlyDictionary<string, object?> arguments)
    {
        var result = await _mcp.Client.CallToolAsync(tool, arguments);

        Assert.True(result.IsError, "The tool call was expected to fail.");

        return string.Join(
            " ",
            result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(block => block.Text));
    }

    private async ValueTask<JsonElement> Call(string tool, IReadOnlyDictionary<string, object?> arguments)
    {
        var result = await _mcp.Client.CallToolAsync(tool, arguments);
        var content = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(result.Content[0]);

        return JsonDocument.Parse(content.Text).RootElement.Clone();
    }

    private static DateTime Next() => s_occurred.AddMinutes(Interlocked.Increment(ref s_sequence));
}
