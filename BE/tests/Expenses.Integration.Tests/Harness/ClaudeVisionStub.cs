using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Expenses.Integration.Tests.Harness;

/// <summary>
/// The Claude Messages API, standing still. It answers from a fixture recorded once against the
/// real shape of a Messages API response, so the suite is deterministic and never reaches
/// `api.anthropic.com` (D26, D34).
///
/// It is a real HTTP server rather than a stubbed handler because the parts most likely to be wrong
/// are the wire parts: that the image reaches it base64-encoded, that the model id is carried, and
/// that a non-200, a hang or an unexpected body is absorbed rather than thrown.
/// </summary>
public sealed class ClaudeVisionStub : IAsyncDisposable
{
    private readonly WebApplication _app;

    private readonly List<JsonElement> _requests = [];

    private ClaudeVisionStub(WebApplication app) => _app = app;

    /// <summary>Every request the engine was asked, in order, as the parsed request body.</summary>
    public IReadOnlyList<JsonElement> Requests
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

    /// <summary>A realistic Messages API response wrapping an extraction JSON payload.</summary>
    public static string RecordedResponse(string fixtureName = "claude-vision-response.json") => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName));

    public static Task<ClaudeVisionStub> Answering(string fixtureName = "claude-vision-response.json")
        => Start(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(RecordedResponse(fixtureName));
        });

    /// <summary>A 200 whose body does not carry the fields extraction expects.</summary>
    public static Task<ClaudeVisionStub> WithUnexpectedShape()
        => Start(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"id":"msg_01Unexpected","type":"message","content":[]}""");
        });

    public static Task<ClaudeVisionStub> Failing(HttpStatusCode status)
        => Start(context =>
        {
            context.Response.StatusCode = (int)status;
            return Task.CompletedTask;
        });

    /// <summary>A service that accepts the request and then says nothing at all.</summary>
    public static Task<ClaudeVisionStub> Hanging()
        => Start(async context => await Task.Delay(TimeSpan.FromMinutes(5), context.RequestAborted));

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static async Task<ClaudeVisionStub> Start(RequestDelegate respond)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        var stub = new ClaudeVisionStub(app);

        app.MapPost("/v1/messages", async context =>
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body);

            lock (stub._requests)
            {
                stub._requests.Add(document.RootElement.Clone());
            }

            await respond(context);
        });

        await app.StartAsync();

        stub.BaseAddress = app.Urls.First();

        return stub;
    }
}
