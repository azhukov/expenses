using System.Net.Http.Json;
using System.Text.Json;
using Expenses.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Expenses.Integration.Tests.Harness;

/// <summary>
/// The real HTTP host over the container database. Nothing is stubbed: the adapter is under test
/// precisely because it must contain no rule of its own (D1), and only running it proves that.
/// </summary>
/// <remarks>
/// The entry point is named by a public type of the API assembly rather than by <c>Program</c>:
/// both hosts have one of those, and the test project references both.
/// </remarks>
public sealed class ExpensesApi(string connectionString, params (string Key, string Value)[] settings)
    : WebApplicationFactory<Expenses.Api.ErrorResponse>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Left unset (the factory's default, the source directory) unless a test needs an isolated
    /// place to look for what the host wrote to disk, e.g. log files under logs/.
    /// </summary>
    public string? ContentRoot { get; init; }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var configuration = new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{ExpensesInfrastructure.ConnectionName}"] = connectionString,

            // The suite drives extraction itself, so the drain would otherwise race the tests for
            // the same images.
            ["Extraction:DrainInBackground"] = "false",
        };

        foreach (var (key, value) in settings)
        {
            configuration[key] = value;
        }

        builder.ConfigureHostConfiguration(host => host.AddInMemoryCollection(configuration));

        if (ContentRoot is not null)
        {
            builder.UseContentRoot(ContentRoot);
        }

        return base.CreateHost(builder);
    }

    /// <summary>Resolves a service from the running host, for arranging or asserting directly.</summary>
    public T Resolve<T>()
        where T : notnull => Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    public static async Task<T> Read<T>(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<T>(Json)
        ?? throw new InvalidOperationException($"The response carried no {typeof(T).Name}.");
}
