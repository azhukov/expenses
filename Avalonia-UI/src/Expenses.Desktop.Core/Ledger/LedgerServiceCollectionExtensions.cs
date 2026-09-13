using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Desktop.Core.Ledger;

public static class LedgerServiceCollectionExtensions
{
    /// <summary>Registers <see cref="LedgerClient"/> as a typed client against the configured address.</summary>
    public static IServiceCollection AddLedgerClient(this IServiceCollection services, IConfiguration configuration)
    {
        var address = LedgerAddress.Resolve(configuration);

        // No client-side timeout beyond HttpClient's default: capture holds its request open for the
        // whole of extraction, and the spec asks only that the wait be visible, not bounded.
        services.AddHttpClient<LedgerClient>(http => http.BaseAddress = address);

        return services;
    }
}
