using Expenses.Infrastructure;
using Expenses.Infrastructure.Persistence;
using Expenses.Mcp;

var transport = Environment.GetEnvironmentVariable("EXPENSES_MCP_TRANSPORT")?.Trim().ToLowerInvariant();

if (transport == "http")
{
    // A hosted deployment, where an assistant reaches the ledger over the network.
    var web = WebApplication.CreateBuilder(args);

    web.Services.AddExpensesInfrastructure(web.Configuration);
    web.Services.AddExpensesMcpServer().WithHttpTransport();

    var hosted = web.Build();

    // Never migrates: this host is a second process against the same database, and two hosts
    // migrating on startup would race (D15).
    await hosted.Services.PrepareExpensesDatabase(applyMigrations: false);

    hosted.MapMcp();
    await hosted.RunAsync();

    return;
}

// stdio is the default, because it is the natural local-assistant experience and is impossible to
// offer from a shared web host (D1). Nothing may be written to stdout but protocol traffic, so
// logging goes to stderr.
var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddExpensesInfrastructure(builder.Configuration);
builder.Services.AddExpensesMcpServer().WithStdioServerTransport();

var host = builder.Build();

await host.Services.PrepareExpensesDatabase(applyMigrations: false);
await host.RunAsync();
