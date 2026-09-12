using System.Diagnostics;
using System.Text;

namespace Expenses.Integration.Tests.Harness;

/// <summary>
/// The real MCP host, run as the real out-of-process executable Program.cs produces — not the
/// in-process <see cref="ExpensesMcp"/> harness, which builds its own host directly and never
/// exercises Program.cs's stdio setup at all. Only a real subprocess can demonstrate that the stdio
/// transport's stdout carries nothing but protocol traffic (D29): logging is process-wide state, and
/// an in-process pipe test would share the test runner's own console configuration instead of the
/// host's.
/// </summary>
public sealed class ExpensesMcpProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _standardError = new();
    private readonly Task _drainStandardError;

    private ExpensesMcpProcess(Process process)
    {
        _process = process;
        _drainStandardError = DrainStandardErrorAsync();
    }

    public Stream StandardInput => _process.StandardInput.BaseStream;

    public Stream StandardOutput => _process.StandardOutput.BaseStream;

    public string StandardErrorText
    {
        get
        {
            lock (_standardError)
            {
                return _standardError.ToString();
            }
        }
    }

    public static ExpensesMcpProcess Start(
        string connectionString,
        string contentRoot,
        params (string Key, string Value)[] environment)
    {
        var startInfo = new ProcessStartInfo("dotnet", $"\"{LocateAssembly()}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = contentRoot,
        };

        startInfo.Environment["ConnectionStrings__Expenses"] = connectionString;

        // The suite drives extraction itself, so the drain would otherwise race the tests for the
        // same images — the same reason the in-process harnesses set this.
        startInfo.Environment["Extraction__DrainInBackground"] = "false";
        startInfo.Environment["Logging__LogLevel__Default"] = "Trace";

        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Expenses.Mcp did not start.");

        return new ExpensesMcpProcess(process);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }

        await _process.WaitForExitAsync();
        await _drainStandardError;
        _process.Dispose();
    }

    private async Task DrainStandardErrorAsync()
    {
        string? line;
        while ((line = await _process.StandardError.ReadLineAsync()) is not null)
        {
            lock (_standardError)
            {
                _standardError.AppendLine(line);
            }
        }
    }

    /// <summary>
    /// Read from the built output rather than restated here: the test assembly and Expenses.Mcp are
    /// siblings under the same repository, found the same way <see cref="PostgresFixture"/> finds
    /// the provisioning script.
    /// </summary>
    private static string LocateAssembly()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);

        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Expenses.sln")))
        {
            repository = repository.Parent;
        }

        if (repository is null)
        {
            throw new InvalidOperationException(
                "Expenses.Mcp.dll could not be located: no Expenses.sln above the test output.");
        }

        string configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

        string assembly = Path.Combine(
            repository.FullName, "Expenses.Mcp", "bin", configuration, "net10.0", "Expenses.Mcp.dll");

        if (!File.Exists(assembly))
        {
            throw new InvalidOperationException(
                $"Expenses.Mcp.dll not found at {assembly}. Build the solution before running this test.");
        }

        return assembly;
    }
}
