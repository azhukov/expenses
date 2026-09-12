using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using MicrosoftLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Expenses.Infrastructure.Logging;

/// <summary>
/// The single entry point both hosts call to get Serilog wired identically (D28). Configures a
/// console sink and a daily-rolling, 31-day-retained file sink under <c>logs/</c> at the host's
/// content root, reading levels from the existing <c>Logging:LogLevel</c> configuration section
/// (D30, D31) — the same Microsoft.Extensions.Logging schema both hosts already use, mapped by hand
/// here because Serilog's own configuration reader understands a different ("Serilog:MinimumLevel")
/// schema, not this one.
/// </summary>
public static class ExpensesLoggingExtensions
{
    private const int RetainedFileCount = 31;

    /// <summary>
    /// When <paramref name="useStandardError"/> is <see langword="true"/>, the console sink writes
    /// every level to stderr instead of stdout (D29) — required by the MCP stdio transport, whose
    /// stdout carries protocol traffic exclusively.
    /// </summary>
    public static IHostApplicationBuilder AddExpensesLogging(
        this IHostApplicationBuilder builder,
        bool useStandardError)
    {
        string logFilePath = Path.Combine(builder.Environment.ContentRootPath, "logs", "expenses-.log");

        builder.Services.AddSerilog((_, configuration) =>
        {
            ApplyLogLevels(configuration, builder.Configuration);

            configuration
                .WriteTo.Console(standardErrorFromLevel: useStandardError ? LevelAlias.Minimum : null)
                .WriteTo.File(
                    logFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: RetainedFileCount);
        });

        return builder;
    }

    private static void ApplyLogLevels(LoggerConfiguration configuration, IConfiguration source)
    {
        var section = source.GetSection("Logging:LogLevel");

        var defaultLevel = section["Default"] is { } value ? ToSerilogLevel(value) : LogEventLevel.Information;
        configuration.MinimumLevel.Is(defaultLevel);

        foreach (var category in section.GetChildren())
        {
            if (category.Key == "Default" || category.Value is null)
            {
                continue;
            }

            configuration.MinimumLevel.Override(category.Key, ToSerilogLevel(category.Value));
        }
    }

    private static LogEventLevel ToSerilogLevel(string value) => Enum.Parse<MicrosoftLogLevel>(value, ignoreCase: true) switch
    {
        MicrosoftLogLevel.Trace => LogEventLevel.Verbose,
        MicrosoftLogLevel.Debug => LogEventLevel.Debug,
        MicrosoftLogLevel.Information => LogEventLevel.Information,
        MicrosoftLogLevel.Warning => LogEventLevel.Warning,
        MicrosoftLogLevel.Error => LogEventLevel.Error,
        MicrosoftLogLevel.Critical => LogEventLevel.Fatal,

        // Serilog has no "suppress everything" level; one past Fatal never matches a real event.
        MicrosoftLogLevel.None => (LogEventLevel)((int)LevelAlias.Maximum + 1),
        _ => LogEventLevel.Information,
    };
}
