using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Expenses.Infrastructure.Persistence;

/// <summary>
/// Readiness: the ledger's database answers. A host whose connection has gone — the database
/// restarted, the password rotated, the network partitioned — still serves its own port, so
/// liveness alone would keep it in rotation while every request that touches the ledger fails.
/// </summary>
/// <remarks>
/// A round trip to the server, not merely a pool entry: <c>CanConnectAsync</c>
/// opens a connection and discards it. Schema is deliberately not re-checked here — migrations and
/// the provisioning assertion run once at startup (D13, D15), and repeating either on every probe
/// would turn a five-second interval into steady load on <c>pg_database</c>.
/// </remarks>
public sealed class DatabaseHealthCheck(ExpensesDbContext ledger) : IHealthCheck
{
    public const string Name = "database";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ledger.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("The ledger database answered.")
                : HealthCheckResult.Unhealthy("The ledger database did not answer.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The exception is carried rather than logged and swallowed: whatever reads the probe
            // is the only thing watching, and "unhealthy" without a reason is a second outage to
            // diagnose.
            return HealthCheckResult.Unhealthy("The ledger database could not be reached.", exception);
        }
    }
}
