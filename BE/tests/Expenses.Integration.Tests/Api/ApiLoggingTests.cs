using Expenses.Integration.Tests.Harness;

namespace Expenses.Integration.Tests.Api;

/// <summary>
/// Scenarios from observability: "Api host console output", "Log file created on startup".
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ApiLoggingTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly string _contentRoot = Directory.CreateTempSubdirectory("expenses-api-logging-tests-").FullName;

    public Task InitializeAsync() => postgres.Migrate();

    public Task DisposeAsync()
    {
        Directory.Delete(_contentRoot, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Log_file_appears_under_logs_with_the_startup_line()
    {
        await using (var api = new ExpensesApi(postgres.ConnectionString) { ContentRoot = _contentRoot })
        {
            // Forces the host to actually start rather than lazily build on first request.
            using var client = api.CreateClient();
        }

        string logsDirectory = Path.Combine(_contentRoot, "logs");
        Assert.True(Directory.Exists(logsDirectory), $"Expected a logs directory at {logsDirectory}.");

        string[] logFiles = Directory.GetFiles(logsDirectory, "*.log");
        Assert.NotEmpty(logFiles);
        Assert.Contains(
            logFiles,
            file => File.ReadAllText(file).Contains("Expenses.Api starting", StringComparison.Ordinal));
    }
}
