namespace Expenses.Api;

/// <summary>
/// Which browser origins may call this host. The browser client is served from an origin of its
/// own and calls the API directly, so the list decides whether it can read the ledger at all.
/// </summary>
public static class CrossOriginAccess
{
    public const string SettingKey = "Cors:AllowedOrigins";

    private const string PolicyName = "ExpensesBrowserClient";

    /// <summary>
    /// Registers the one policy, over the origins configured under <see cref="SettingKey"/>. An absent
    /// or empty list allows no origin at all, so a host nobody configured fails closed.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// An entry is not a plain http(s) origin. Refused at startup, because a wildcard or a path would
    /// otherwise surface only as a browser silently refusing every call.
    /// </exception>
    public static IServiceCollection AddExpensesCrossOriginAccess(this IServiceCollection services, IConfiguration configuration)
    {
        string[] origins = ReadAllowedOrigins(configuration);

        return services.AddCors(options => options.AddPolicy(PolicyName, policy => policy
            .WithOrigins(origins)

            // Exactly what the client sends and nothing more: there is no authentication to lean on,
            // so the allowed surface should not be wider than the one in use.
            .WithMethods("GET", "POST", "PUT", "DELETE")
            .WithHeaders("accept", "content-type")
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));
    }

    /// <summary>
    /// The headers are added when the response starts, so a failure written by the error handler
    /// carries them too, and a client on another origin can still read its message.
    /// </summary>
    public static IApplicationBuilder UseExpensesCrossOriginAccess(this WebApplication app)
        => app.UseCors(PolicyName);

    /// <summary>
    /// The origins configured under <see cref="SettingKey"/>, normalised exactly as the policy takes
    /// them. Public so a host can log what it will actually allow rather than a second reading of the
    /// configuration that could disagree with the registered policy.
    /// </summary>
    /// <exception cref="InvalidOperationException">An entry is not a plain http(s) origin.</exception>
    public static string[] ReadAllowedOrigins(IConfiguration configuration)
        => [.. (configuration.GetSection(SettingKey).Get<string[]>() ?? []).Select(Normalise)];

    private static string Normalise(string entry)
    {
        bool valid = entry != "*"
            && Uri.TryCreate(entry, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.AbsolutePath == "/"
            && uri.Query.Length == 0
            && uri.Fragment.Length == 0
            && string.IsNullOrEmpty(uri.UserInfo);

        return valid
            ? entry.TrimEnd('/')
            : throw new InvalidOperationException(
                $"'{entry}' under {SettingKey} is not an origin. Each entry must be a scheme, host and optional port, such as http://localhost:5173, with no path, query or wildcard.");
    }
}
