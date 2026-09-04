using Expenses.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Expenses.Integration.Tests.Logging;

/// <summary>
/// Scenarios from observability: "Api host console output", "Mcp http-transport console output",
/// "Mcp stdio-transport console output stays off stdout", "Log file created on startup", "Default
/// level applies", "Category override applies". Builds a real host with real Serilog sinks — no
/// database needed, so this class does not join <c>PostgresCollection</c>.
/// </summary>
public sealed class ExpensesLoggingTests : IDisposable
{
    private readonly string _contentRoot = Directory.CreateTempSubdirectory("expenses-logging-tests-").FullName;
    private readonly TextWriter _originalOut = Console.Out;
    private readonly TextWriter _originalError = Console.Error;

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
        Directory.Delete(_contentRoot, recursive: true);
    }

    [Fact]
    public void Console_output_defaults_to_standard_output()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);

        using var host = BuildHost(useStandardError: false);
        host.Services.GetRequiredService<ILogger<ExpensesLoggingTests>>()
            .LogInformation("marker-console-defaults-to-stdout");

        Assert.Contains("marker-console-defaults-to-stdout", stdout.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("marker-console-defaults-to-stdout", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Console_output_moves_to_standard_error_when_requested()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);

        using var host = BuildHost(useStandardError: true);
        host.Services.GetRequiredService<ILogger<ExpensesLoggingTests>>()
            .LogInformation("marker-console-moves-to-stderr");

        Assert.Contains("marker-console-moves-to-stderr", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("marker-console-moves-to-stderr", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Log_file_is_created_under_logs_directory()
    {
        using (var host = BuildHost(useStandardError: false))
        {
            host.Services.GetRequiredService<ILogger<ExpensesLoggingTests>>()
                .LogInformation("marker-file-created");
        }

        var logsDirectory = Path.Combine(_contentRoot, "logs");
        Assert.True(Directory.Exists(logsDirectory), $"Expected a logs directory at {logsDirectory}.");

        var logFiles = Directory.GetFiles(logsDirectory, "*.log");
        Assert.NotEmpty(logFiles);
        Assert.Contains(logFiles, file => File.ReadAllText(file).Contains("marker-file-created", StringComparison.Ordinal));
    }

    [Fact]
    public void Default_level_applies()
    {
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        using var host = BuildHost(useStandardError: false, ("Logging:LogLevel:Default", "Warning"));
        var logger = host.Services.GetRequiredService<ILogger<ExpensesLoggingTests>>();

        logger.LogInformation("marker-below-default-level");
        logger.LogWarning("marker-at-default-level");

        var output = stdout.ToString();
        Assert.DoesNotContain("marker-below-default-level", output, StringComparison.Ordinal);
        Assert.Contains("marker-at-default-level", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Category_override_applies()
    {
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        using var host = BuildHost(
            useStandardError: false,
            ("Logging:LogLevel:Default", "Warning"),
            ($"Logging:LogLevel:{typeof(ExpensesLoggingTests).FullName}", "Debug"));

        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var overriddenLogger = loggerFactory.CreateLogger(typeof(ExpensesLoggingTests).FullName!);
        var defaultLogger = loggerFactory.CreateLogger("Expenses.Integration.Tests.Logging.SomeOtherCategory");

        overriddenLogger.LogDebug("marker-category-override-emits");
        defaultLogger.LogDebug("marker-category-without-override-suppressed");

        var output = stdout.ToString();
        Assert.Contains("marker-category-override-emits", output, StringComparison.Ordinal);
        Assert.DoesNotContain("marker-category-without-override-suppressed", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Old_log_files_are_cleaned_up()
    {
        var logsDirectory = Directory.CreateDirectory(Path.Combine(_contentRoot, "logs"));
        var today = DateTime.UtcNow.Date;

        // 40 days of pre-existing daily files, one per day up to yesterday — well past the 31-day
        // retention limit before the host even starts.
        for (var day = 1; day <= 40; day++)
        {
            File.WriteAllText(
                Path.Combine(logsDirectory.FullName, $"expenses-{today.AddDays(-day):yyyyMMdd}.log"),
                "pre-existing");
        }

        using (var host = BuildHost(useStandardError: false))
        {
            host.Services.GetRequiredService<ILogger<ExpensesLoggingTests>>()
                .LogInformation("marker-triggers-retention");
        }

        var remaining = Directory.GetFiles(logsDirectory.FullName, "*.log");

        // "Approximately" 31: the sink counts today's own file among the retained ones, so 41
        // files existed (40 pre-existing + today's) and it prunes down to its limit.
        Assert.True(
            remaining.Length <= 31,
            $"Expected at most 31 retained log files, found {remaining.Length}.");

        var oldestPreExisting = Path.Combine(logsDirectory.FullName, $"expenses-{today.AddDays(-40):yyyyMMdd}.log");
        Assert.DoesNotContain(oldestPreExisting, remaining);
    }

    private IHost BuildHost(bool useStandardError, params (string Key, string Value)[] settings)
    {
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { ContentRootPath = _contentRoot });

        var configuration = new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Information",
        };

        foreach (var (key, value) in settings)
        {
            configuration[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(configuration);
        builder.AddExpensesLogging(useStandardError);

        return builder.Build();
    }
}
