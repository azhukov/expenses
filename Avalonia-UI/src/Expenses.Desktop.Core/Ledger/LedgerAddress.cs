using Microsoft.Extensions.Configuration;

namespace Expenses.Desktop.Core.Ledger;

/// <summary>
/// Where the ledger's HTTP interface is (D4): the <c>EXPENSES_API</c> environment variable, else
/// <c>Ledger:BaseAddress</c> from <c>appsettings.json</c> beside the executable, else the address the
/// local stack publishes.
/// </summary>
public static class LedgerAddress
{
    public const string Default = "http://localhost:5082/";

    public const string EnvironmentVariable = "EXPENSES_API";

    public const string SettingKey = "Ledger:BaseAddress";

    /// <summary>The configuration the address is read from: the settings file, then the environment.</summary>
    public static IConfiguration Configuration(string directory)
    {
        return new ConfigurationBuilder()
            .SetBasePath(directory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    /// <summary>The base address, always ending in a slash so relative paths keep any path it has.</summary>
    public static Uri Resolve(IConfiguration configuration)
    {
        var configured = configuration[EnvironmentVariable] is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : configuration[SettingKey];

        var address = string.IsNullOrWhiteSpace(configured) ? Default : configured.Trim();

        return new Uri(address.EndsWith('/') ? address : address + "/");
    }
}
