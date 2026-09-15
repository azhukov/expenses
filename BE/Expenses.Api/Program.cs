using System.Text.Json.Serialization;
using Expenses.Api;
using Expenses.Api.Controllers;
using Expenses.Infrastructure;
using Expenses.Infrastructure.Logging;
using Expenses.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog first, so every log line from here on — including infrastructure wiring below — goes
// through it (D28).
builder.AddExpensesLogging(useStandardError: false);

// One call into Infrastructure and nothing else from it (D1): the composition root lives there so
// that this host and the MCP host cannot drift apart in how they are wired.
builder.Services.AddExpensesInfrastructure(builder.Configuration);

// Enums travel as their names: `"Pending"` is a contract a client can read and keep working
// against, where `0` silently changes meaning the day a member is inserted.
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// A request MVC cannot bind leaves through the same shape as every other failure, rather than as
// the ProblemDetails the framework would write on its own.
builder.Services.Configure<ApiBehaviorOptions>(options =>
    options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(
        new ErrorResponse(
            "request_invalid",
            "The request could not be read.",
            context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .ToDictionary(
                    entry => entry.Key,
                    entry => (object?)string.Join(" ", entry.Value!.Errors.Select(error => error.ErrorMessage))))));

// The error handler writes with WriteAsJsonAsync, which reads these options rather than MVC's.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The contract the React client will work from (D1). Generated output, not hand-written; Swagger UI
// below is a reader over this same document, so the two cannot disagree.
builder.Services.AddOpenApi();

// The browser client is served from an origin of its own, so which origins may call is configuration.
builder.Services.AddExpensesCrossOriginAccess(builder.Configuration);

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    // The limit is refused before the body is buffered rather than after (D11).
    options.MultipartBodyLengthLimit = CapturesController.MaximumUploadBytes;
});

var app = builder.Build();

// Outermost, so a request is judged before anything else answers it. The headers are added when the
// response starts, so an error the handler below writes carries them too (CorsTests pins that).
app.UseExpensesCrossOriginAccess();
app.UseExpensesErrors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Expenses API"));

    // Migrating on startup is a development convenience only: two hosts doing it anywhere else
    // would race, and neither should hold DDL rights at runtime (D15).
    await app.Services.PrepareExpensesDatabase(applyMigrations: true);
}
else
{
    // The provisioning assertion runs everywhere, because a mis-provisioned database looks
    // healthy and merely sorts wrongly (D13).
    await app.Services.PrepareExpensesDatabase(applyMigrations: false);
}

app.MapControllers();

app.Logger.LogInformation("Expenses.Api starting.");

try
{
    app.Run();
}
catch (Exception ex)
{
    // Caught here rather than left to crash silently: whatever the console shows, the file sink
    // keeps a durable copy after the process is gone (D32).
    Log.Fatal(ex, "Expenses.Api terminated unexpectedly.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
