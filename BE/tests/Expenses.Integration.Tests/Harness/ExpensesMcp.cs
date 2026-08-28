using System.IO.Pipelines;
using Expenses.Infrastructure;
using Expenses.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Expenses.Integration.Tests.Harness;

/// <summary>
/// The real MCP server, spoken to by a real MCP client over a pair of in-process pipes. Nothing is
/// stubbed: tool discovery, argument schemas and error reporting are all things only the protocol
/// can demonstrate, and they are exactly what a test of this adapter is for.
/// </summary>
public sealed class ExpensesMcp : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly Pipe _clientToServer = new();
    private readonly Pipe _serverToClient = new();

    private ExpensesMcp(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{ExpensesInfrastructure.ConnectionName}"] = connectionString,

            // The suite drives extraction itself, so a drain would race it for the same images.
            ["Extraction:DrainInBackground"] = "false",
        });

        builder.Services.AddExpensesInfrastructure(builder.Configuration);
        builder.Services
            .AddExpensesMcpServer()
            .WithStreamServerTransport(_clientToServer.Reader.AsStream(), _serverToClient.Writer.AsStream());

        _host = builder.Build();
    }

    public McpClient Client { get; private set; } = null!;

    public IServiceProvider Services => _host.Services;

    public static async Task<ExpensesMcp> Start(string connectionString)
    {
        var server = new ExpensesMcp(connectionString);
        await server._host.StartAsync();

        server.Client = await McpClient.CreateAsync(new StreamClientTransport(
            server._clientToServer.Writer.AsStream(),
            server._serverToClient.Reader.AsStream()));

        return server;
    }

    /// <summary>Resolves a use case, for arranging state the tools then read.</summary>
    public T Resolve<T>()
        where T : notnull => _host.Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _host.StopAsync();
        _host.Dispose();
    }
}
