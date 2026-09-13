using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Expenses.Desktop.Core.Tests.Fakes;

/// <summary>
/// The ledger's HTTP interface, faked at the transport. Responses are keyed by method and path
/// (without the query), and every request is recorded with its body read out before the client can
/// dispose it, so a test can assert on exactly what went over the wire.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    /// <summary>What the API sends: camelCase, enums as their names.</summary>
    public static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> _routes = [];

    public List<RecordedRequest> Requests { get; } = [];

    public static HttpClient ClientFor(FakeHttpMessageHandler handler, string baseAddress = "http://ledger.test/")
    {
        return new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
    }

    public FakeHttpMessageHandler Respond(string method, string path, HttpStatusCode status, string json)
    {
        _routes[Key(method, path)] = _ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

        return this;
    }

    public FakeHttpMessageHandler Respond(string method, string path, HttpStatusCode status, object body)
    {
        return Respond(method, path, status, JsonSerializer.Serialize(body, ApiJson));
    }

    public FakeHttpMessageHandler Respond(string method, string path, Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        _routes[Key(method, path)] = responder;

        return this;
    }

    public FakeHttpMessageHandler Unreachable(string method, string path)
    {
        _routes[Key(method, path)] = _ => throw new HttpRequestException("Connection refused");

        return this;
    }

    public IEnumerable<RecordedRequest> To(string method, string path)
    {
        return Requests.Where(each => each.Method == method && each.Uri.AbsolutePath == path);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = request.Content?.Headers.ContentType?.ToString();

        Requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!, body, contentType));

        if (!_routes.TryGetValue(Key(request.Method.Method, request.RequestUri!.AbsolutePath), out var responder))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        return await responder(request);
    }

    private static string Key(string method, string path)
    {
        return $"{method} {path}";
    }
}
