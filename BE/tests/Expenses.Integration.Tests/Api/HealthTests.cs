using System.Net;
using System.Text.Json;
using Expenses.Api;
using Expenses.Infrastructure.Persistence;
using Expenses.Integration.Tests.Harness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Expenses.Integration.Tests.Api;

/// <summary>
/// The probes a deployment routes on: liveness answers for the process alone, readiness for the
/// database behind it.
/// </summary>
/// <remarks>
/// The unreachable database is put to the check directly rather than to a host, because a host
/// pointed at one never finishes starting — <c>PrepareExpensesDatabase</c> verifies provisioning in
/// every environment (D13), so there would be no endpoint left to probe. What the host tests pin
/// instead is that liveness consults nothing at all, which is the same guarantee from the other
/// side: no dependency can take it down.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class HealthTests(PostgresFixture postgres) : IAsyncLifetime
{
    /// <summary>Port 9 is discard: nothing listens, so the connection is refused rather than hanging.</summary>
    private const string Unreachable =
        "Host=127.0.0.1;Port=9;Database=expenses;Username=expenses;Password=expenses;Timeout=2";

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
    public async Task Liveness_answers_without_consulting_anything()
    {
        using var client = Host().CreateClient();

        var response = await client.GetAsync(HealthEndpoints.LivenessPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await Status(response));
        Assert.Empty(await Checks(response));
    }

    [Fact]
    public async Task Readiness_reports_the_database_it_reached()
    {
        using var client = Host().CreateClient();

        var response = await client.GetAsync(HealthEndpoints.ReadinessPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await Status(response));

        var database = Assert.Single(await Checks(response));
        Assert.Equal(DatabaseHealthCheck.Name, database.GetProperty("name").GetString());
        Assert.Equal("Healthy", database.GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_database_check_is_unhealthy_when_the_database_cannot_be_reached()
    {
        await using var context = new ExpensesDbContext(
            new DbContextOptionsBuilder<ExpensesDbContext>().UseNpgsql(Unreachable).Options);

        var result = await new DatabaseHealthCheck(context).CheckHealthAsync(
            new HealthCheckContext { Registration = Registration },
            CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public async Task The_database_check_is_healthy_against_the_real_database()
    {
        await using var context = new ExpensesDbContext(
            new DbContextOptionsBuilder<ExpensesDbContext>().UseNpgsql(postgres.ConnectionString).Options);

        var result = await new DatabaseHealthCheck(context).CheckHealthAsync(
            new HealthCheckContext { Registration = Registration },
            CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    /// <summary>The check reads nothing off its registration; it is here because the context requires one.</summary>
    private static HealthCheckRegistration Registration => new(
        DatabaseHealthCheck.Name,
        _ => throw new InvalidOperationException("The check under test is constructed directly."),
        HealthStatus.Unhealthy,
        [HealthTags.Ready]);

    private ExpensesApi Host()
    {
        var host = new ExpensesApi(postgres.ConnectionString);
        _hosts.Add(host);

        return host;
    }

    private static async Task<string?> Status(HttpResponseMessage response)
        => (await Body(response)).GetProperty("status").GetString();

    private static async Task<JsonElement[]> Checks(HttpResponseMessage response)
        => [.. (await Body(response)).GetProperty("checks").EnumerateArray()];

    private static async Task<JsonElement> Body(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
