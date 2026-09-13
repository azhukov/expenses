using System.Net;
using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Expenses.Desktop.Core.Tests.Ledger;

/// <summary>
/// Scenarios from desktop-client: "The default address reaches the local stack", "A configured
/// address is used". Serialised with the other test class that touches the process environment.
/// </summary>
[Collection(ProcessEnvironment.Name)]
public sealed class LedgerAddressTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("expenses-desktop-").FullName;
    private readonly string? _previous = Environment.GetEnvironmentVariable(LedgerAddress.EnvironmentVariable);

    public LedgerAddressTests()
    {
        Environment.SetEnvironmentVariable(LedgerAddress.EnvironmentVariable, null);
    }

    [Fact]
    public void The_default_address_reaches_the_local_stack()
    {
        var address = LedgerAddress.Resolve(LedgerAddress.Configuration(_directory));

        Assert.Equal(new Uri("http://localhost:5082/"), address);
    }

    [Fact]
    public void The_settings_file_overrides_the_default()
    {
        File.WriteAllText(Path.Combine(_directory, "appsettings.json"), """{ "Ledger": { "BaseAddress": "http://ledger.lan:8080" } }""");

        var address = LedgerAddress.Resolve(LedgerAddress.Configuration(_directory));

        Assert.Equal(new Uri("http://ledger.lan:8080/"), address);
    }

    [Fact]
    public void The_environment_overrides_the_settings_file()
    {
        File.WriteAllText(Path.Combine(_directory, "appsettings.json"), """{ "Ledger": { "BaseAddress": "http://ledger.lan:8080" } }""");
        Environment.SetEnvironmentVariable(LedgerAddress.EnvironmentVariable, "http://127.0.0.1:5999/api");

        var address = LedgerAddress.Resolve(LedgerAddress.Configuration(_directory));

        Assert.Equal(new Uri("http://127.0.0.1:5999/api/"), address);
    }

    [Fact]
    public async Task A_configured_address_is_used_by_the_registered_client()
    {
        var api = new FakeHttpMessageHandler().Respond("GET", "/api/units", HttpStatusCode.OK, "[]");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [LedgerAddress.SettingKey] = "http://ledger.lan:8080/api" })
            .Build();

        var services = new ServiceCollection().AddLedgerClient(configuration);
        services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = api));
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<LedgerClient>().ListUnits(TestContext.Current.CancellationToken);

        Assert.Equal("http://ledger.lan:8080/api/units", Assert.Single(api.Requests).Uri.ToString());
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(LedgerAddress.EnvironmentVariable, _previous);
        Directory.Delete(_directory, recursive: true);
    }
}
