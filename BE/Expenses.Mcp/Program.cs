using Expenses.Infrastructure;
using Expenses.Infrastructure.Logging;
using Expenses.Infrastructure.Persistence;
using Expenses.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

var transport = Environment.GetEnvironmentVariable("EXPENSES_MCP_TRANSPORT")?.Trim().ToLowerInvariant();

if (transport == "http")
{
    // A hosted deployment, where an assistant reaches the ledger over the network.
    var web = WebApplication.CreateBuilder(args);

    web.AddExpensesLogging(useStandardError: false);

    web.Services.AddExpensesInfrastructure(web.Configuration);
    web.Services.AddExpensesMcpServer().WithHttpTransport();

    var hosted = web.Build();

    // Never migrates: this host is a second process against the same database, and two hosts
    // migrating on startup would race (D15).
    await hosted.Services.PrepareExpensesDatabase(applyMigrations: false);

    hosted.MapMcp();

    hosted.Logger.LogInformation("Expenses.Mcp starting (http transport).");

    try
    {
        await hosted.RunAsync();
    }
    catch (Exception ex)
    {
        // Caught here rather than left to crash silently: whatever the console shows, the file
        // sink keeps a durable copy after the process is gone (D32).
        Log.Fatal(ex, "Expenses.Mcp (http transport) terminated unexpectedly.");
        throw;
    }
    finally
    {
        Log.CloseAndFlush();
    }

    return;
}

// stdio is the default, because it is the natural local-assistant experience and is impossible to
// offer from a shared web host (D1). Nothing may be written to stdout but protocol traffic, so
// logging goes to stderr (D29).
var builder = Host.CreateApplicationBuilder(args);

builder.AddExpensesLogging(useStandardError: true);

builder.Services.AddExpensesInfrastructure(builder.Configuration);
builder.Services.AddExpensesMcpServer().WithStdioServerTransport();

var host = builder.Build();

await host.Services.PrepareExpensesDatabase(applyMigrations: false);

host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Expenses.Mcp")
    .LogInformation("Expenses.Mcp starting (stdio transport).");

try
{
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Expenses.Mcp (stdio transport) terminated unexpectedly.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
