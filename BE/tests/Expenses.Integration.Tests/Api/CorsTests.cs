using System.Net;
using System.Text;
using System.Text.Json;
using Expenses.Integration.Tests.Harness;

namespace Expenses.Integration.Tests.Api;

/// <summary>
/// Scenarios from api-surface: "Browser origins are allowed by configuration".
/// </summary>
/// <remarks>
/// Every host here runs outside Development, so the origins under test are exactly the ones each
/// test configures and never the local ones appsettings.Development.json adds.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class CorsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Allowed = "http://client.test:5173";

    private readonly List<ExpensesApi> _hosts = [];

    public Task InitializeAsync() => postgres.Migrate();

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task An_allowed_origin_reads_the_ledger()
    {
        using var client = Host(Allowed).CreateClient();

        var response = await client.SendAsync(FromOrigin(HttpMethod.Get, "/categories", Allowed));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Allowed, AllowedOrigin(response));
    }

    [Fact]
    public async Task An_unlisted_origin_is_not_allowed()
    {
        using var client = Host(Allowed).CreateClient();

        var response = await client.SendAsync(FromOrigin(HttpMethod.Get, "/categories", "http://elsewhere.test"));

        Assert.Null(AllowedOrigin(response));
    }

    [Theory]
    [InlineData("https://client.test:5173")]
    [InlineData("http://client.test:4173")]
    public async Task Origins_match_exactly(string origin)
    {
        using var client = Host(Allowed).CreateClient();

        var response = await client.SendAsync(FromOrigin(HttpMethod.Get, "/categories", origin));

        Assert.Null(AllowedOrigin(response));
    }

    [Fact]
    public async Task Nothing_configured_allows_nothing()
    {
        using var client = Host().CreateClient();

        var response = await client.SendAsync(FromOrigin(HttpMethod.Get, "/categories", Allowed));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(AllowedOrigin(response));
    }

    [Fact]
    public async Task A_preflight_for_a_write_is_answered()
    {
        using var client = Host(Allowed).CreateClient();

        var response = await client.SendAsync(Preflight("/purchases", "POST", "content-type"));

        Assert.Equal(Allowed, AllowedOrigin(response));
        Assert.Contains("POST", Header(response, "Access-Control-Allow-Methods"), StringComparison.Ordinal);
        Assert.Contains("content-type", Header(response, "Access-Control-Allow-Headers"), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Methods_beyond_POST_are_allowed(string method)
    {
        using var client = Host(Allowed).CreateClient();

        var response = await client.SendAsync(Preflight("/categories/food", method, null));

        Assert.Equal(Allowed, AllowedOrigin(response));
        Assert.Contains(method, Header(response, "Access-Control-Allow-Methods"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_error_response_is_readable_cross_origin_when_the_request_cannot_be_read()
    {
        using var client = Host(Allowed).CreateClient();
        var request = FromOrigin(HttpMethod.Post, "/purchases", Allowed);
        request.Content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        await AssertReadableError(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_error_response_is_readable_cross_origin_when_the_resource_is_unknown()
    {
        using var client = Host(Allowed).CreateClient();

        var response = await client.SendAsync(FromOrigin(HttpMethod.Get, "/purchases/987654321", Allowed));

        await AssertReadableError(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_error_response_is_readable_cross_origin_when_the_failure_is_unexpected()
    {
        using var client = Host(Allowed).CreateClient();

        var response = await client.SendAsync(FromOrigin(HttpMethod.Get, "/diagnostics/failure", Allowed));

        await AssertReadableError(response, HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Credentials_are_not_allowed()
    {
        using var client = Host(Allowed).CreateClient();

        var simple = await client.SendAsync(FromOrigin(HttpMethod.Get, "/categories", Allowed));
        var preflight = await client.SendAsync(Preflight("/purchases", "POST", "content-type"));

        Assert.False(simple.Headers.Contains("Access-Control-Allow-Credentials"));
        Assert.False(preflight.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Requests_without_an_origin_are_unaffected()
    {
        using var client = Host(Allowed).CreateClient();

        var response = await client.GetAsync("/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(AllowedOrigin(response));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("http://localhost:5173/app")]
    [InlineData("http://localhost:5173?x=1")]
    [InlineData("ftp://localhost:5173")]
    [InlineData("localhost:5173")]
    public void An_invalid_origin_stops_the_host(string origin)
    {
        var host = Host(Allowed, origin);

        var error = Record.Exception(() => host.CreateClient());

        Assert.NotNull(error);
        Assert.Contains(origin, error.ToString(), StringComparison.Ordinal);
    }

    private static HttpRequestMessage FromOrigin(HttpMethod method, string path, string origin)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Origin", origin);
        return request;
    }

    private static HttpRequestMessage Preflight(string path, string method, string? headers)
    {
        var request = FromOrigin(HttpMethod.Options, path, Allowed);
        request.Headers.Add("Access-Control-Request-Method", method);
        if (headers is not null)
        {
            request.Headers.Add("Access-Control-Request-Headers", headers);
        }

        return request;
    }

    private static string? AllowedOrigin(HttpResponseMessage response)
        => response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? string.Join(",", values) : null;

    private static string Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : string.Empty;

    private static async Task AssertReadableError(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        var error = await ExpensesApi.Read<JsonElement>(response);
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("code").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
        Assert.Equal(Allowed, AllowedOrigin(response));
    }

    private ExpensesApi Host(params string[] origins)
    {
        var host = new ExpensesApi(
            postgres.ConnectionString,
            [.. origins.Select((origin, index) => ($"Cors:AllowedOrigins:{index}", origin))])
        {
            Environment = "Testing",
        };

        _hosts.Add(host);
        return host;
    }
}
