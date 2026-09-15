using System.Net;
using Expenses.Integration.Tests.Harness;
using Expenses.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Integration.Tests.Api;

/// <summary>
/// Scenarios from api-surface: "The host has a Railway environment".
/// </summary>
/// <remarks>
/// No origin or migration setting is passed in: what is under test is exactly what
/// appsettings.Railway.json gives a host started as <c>Railway</c>.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class RailwayEnvironmentTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string RailwayClient = "https://expenses-frontend-production-2e52.up.railway.app";

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
    public async Task The_Railway_clients_origin_reads_the_ledger()
    {
        using var client = Host(postgres.ConnectionString).CreateClient();

        var response = await client.SendAsync(FromOrigin("/categories", RailwayClient));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(RailwayClient, AllowedOrigin(response));
    }

    [Fact]
    public async Task Local_development_origins_are_not_allowed_on_Railway()
    {
        using var client = Host(postgres.ConnectionString).CreateClient();

        var response = await client.SendAsync(FromOrigin("/categories", "http://localhost:5173"));

        Assert.Null(AllowedOrigin(response));
    }

    [Fact]
    public async Task Railway_migrates_on_startup()
    {
        string connection = await postgres.ProvisionAnother("startup_railway");

        _ = Host(connection).Services;

        await using var context = new ExpensesDbContext(
            new DbContextOptionsBuilder<ExpensesDbContext>().UseNpgsql(connection).Options);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    private ExpensesApi Host(string connection)
    {
        var host = new ExpensesApi(connection) { Environment = "Railway" };
        _hosts.Add(host);
        return host;
    }

    private static HttpRequestMessage FromOrigin(string path, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Origin", origin);
        return request;
    }

    private static string? AllowedOrigin(HttpResponseMessage response)
        => response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? string.Join(",", values) : null;
}
