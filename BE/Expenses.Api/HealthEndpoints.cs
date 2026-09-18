using Expenses.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Expenses.Api;

/// <summary>
/// The two probes a deployment needs, kept apart on purpose. <c>/health</c> answers only for this
/// process, so a restarter never kills a host because a dependency blinked; <c>/health/ready</c>
/// answers for everything the host must reach before a request is worth routing to it.
/// </summary>
/// <remarks>
/// Both are cheap and unauthenticated, which is what a container runtime, a load balancer and
/// Railway all assume. They replace <c>/openapi/v1.json</c> and <c>/units</c> as the healthcheck
/// targets: the first exists only in Development, and the second was a ledger read standing in for
/// a probe, which meant a probe that grew a page of rows as the ledger did.
/// </remarks>
public static class HealthEndpoints
{
    public const string LivenessPath = "/health";

    public const string ReadinessPath = "/health/ready";

    public static WebApplication MapExpensesHealth(this WebApplication app)
    {
        // No check at all, not even a fast one: this answers "Kestrel is listening and the process
        // is not wedged", and any dependency consulted here would make it answer something else.
        app.MapHealthChecks(LivenessPath, new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = Write,
        });

        app.MapHealthChecks(ReadinessPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthTags.Ready),
            ResponseWriter = Write,
        });

        return app;
    }

    /// <summary>
    /// Names each check and what it said. The status code is what a probe reads, but the body is
    /// what a person reads at 3am, and "Unhealthy" with no name attached explains nothing.
    /// </summary>
    private static async Task Write(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            durationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,

                // The message only. A stack trace over an unauthenticated endpoint tells a stranger
                // more about the deployment than it tells the operator, who has the logs.
                error = entry.Value.Exception?.Message,
            }),
        });
    }
}
