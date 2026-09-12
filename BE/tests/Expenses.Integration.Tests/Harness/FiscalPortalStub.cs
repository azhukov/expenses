using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Expenses.Integration.Tests.Harness;

/// <summary>
/// The fiscal verification service, standing still. It answers from the response recorded once
/// against the real portal and committed beside the photographs, so the suite is deterministic and
/// never reaches `mapr.tax.gov.me` (D26).
///
/// It is a real HTTP server rather than a stubbed handler because the parts most likely to be wrong
/// are the wire parts: that the request is form-encoded, that the timestamp survives with its `+`
/// intact, and that a non-200 or a silence is absorbed rather than thrown.
/// </summary>
public sealed class FiscalPortalStub : IAsyncDisposable
{
    private readonly WebApplication _app;

    private readonly List<IReadOnlyDictionary<string, string>> _requests = [];

    private FiscalPortalStub(WebApplication app) => _app = app;

    /// <summary>Every request the portal was asked, in order, as the form fields it carried.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public string BaseAddress { get; private set; } = string.Empty;

    /// <summary>The response the real portal gave for the receipt that decodes.</summary>
    public static string RecordedInvoice() => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "verify-32AA324CFF5030271E16D59F7F8EF636.json"));

    public static Task<FiscalPortalStub> Answering()
        => Start(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(RecordedInvoice());
        });

    /// <summary>A service that has no record of the invoice: 200, and nothing in it.</summary>
    public static Task<FiscalPortalStub> WithNoRecord()
        => Start(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(string.Empty);
        });

    public static Task<FiscalPortalStub> Failing(HttpStatusCode status)
        => Start(context =>
        {
            context.Response.StatusCode = (int)status;
            return Task.CompletedTask;
        });

    /// <summary>A service that accepts the request and then says nothing at all.</summary>
    public static Task<FiscalPortalStub> Hanging()
        => Start(async context => await Task.Delay(TimeSpan.FromMinutes(5), context.RequestAborted));

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static async Task<FiscalPortalStub> Start(RequestDelegate respond)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        var stub = new FiscalPortalStub(app);

        app.MapPost("/ic/api/verifyInvoice", async context =>
        {
            var form = context.Request.HasFormContentType
                ? (await context.Request.ReadFormAsync()).ToDictionary(
                    field => field.Key,
                    field => field.Value.ToString())
                : [];

            lock (stub._requests)
            {
                stub._requests.Add(form);
            }

            await respond(context);
        });

        await app.StartAsync();

        stub.BaseAddress = app.Urls.First();

        return stub;
    }
}
