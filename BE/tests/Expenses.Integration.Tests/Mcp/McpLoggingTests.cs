using Expenses.Integration.Tests.Harness;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Expenses.Integration.Tests.Mcp;

/// <summary>
/// Scenarios from observability: "Mcp stdio-transport console output stays off stdout", "Mcp
/// http-transport console output". Runs the real Expenses.Mcp executable, not the in-process
/// harness the other MCP tests use, because only a real process has a real stdout to keep clean.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class McpLoggingTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly string _contentRoot = Directory.CreateTempSubdirectory("expenses-mcp-logging-tests-").FullName;

    public Task InitializeAsync() => postgres.Migrate();

    public Task DisposeAsync()
    {
        Directory.Delete(_contentRoot, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Stdio_transport_speaks_only_protocol_on_stdout()
    {
        await using var process = ExpensesMcpProcess.Start(postgres.ConnectionString, _contentRoot);

        // A real MCP client, parsing the process's real stdout as newline-delimited JSON-RPC: if a
        // log line ever lands on stdout instead of stderr, framing breaks and this throws or hangs
        // rather than quietly succeeding.
        await using var client = await McpClient.CreateAsync(
            new StreamClientTransport(process.StandardInput, process.StandardOutput));

        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, tool => tool.Name == "record_purchase");

        Assert.Contains("Expenses.Mcp starting (stdio transport)", process.StandardErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_transport_console_output_goes_to_stdout()
    {
        await using var process = ExpensesMcpProcess.Start(
            postgres.ConnectionString,
            _contentRoot,
            ("EXPENSES_MCP_TRANSPORT", "http"),
            ("ASPNETCORE_URLS", "http://127.0.0.1:0"));

        using var reader = new StreamReader(process.StandardOutput);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        var seen = string.Empty;

        while (DateTime.UtcNow < deadline && !seen.Contains("Expenses.Mcp starting (http transport)", StringComparison.Ordinal))
        {
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (line is null)
            {
                break;
            }

            seen += line + Environment.NewLine;
        }

        Assert.Contains("Expenses.Mcp starting (http transport)", seen, StringComparison.Ordinal);
    }
}
