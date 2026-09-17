using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Expenses.Infrastructure.Persistence;

/// <summary>
/// Readiness: the ledger's database answers. A host whose connection has gone — the database
/// restarted, the password rotated, the network partitioned — still serves its own port, so
/// liveness alone would keep it in rotation while every request that touches the ledger fails.
/// </summary>
/// <remarks>
/// A statement, because nothing cheaper actually reaches the server: opening a connection is
/// served from Npgsql's pool without a round trip, so a probe built on it reports a stopped
/// database as healthy — observed, with the database stopped underneath a running host.
/// <c>CanConnectAsync</c> does query, but reports the outcome as a bare <c>false</c>, and the
/// reason the database refused — wrong password, no route, too many clients — is the whole content
/// of the probe once it starts failing, so it must reach the body.
///
/// Schema is deliberately not re-checked: migrations and the provisioning assertion run once at
/// startup (D13, D15), and repeating either on every probe would turn a five-second interval into
/// steady load on <c>pg_database</c>.
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
            await ledger.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);

            return HealthCheckResult.Healthy("The ledger database answered.");
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
