namespace Maran.Host.HealthChecks;

/// <summary>
/// The panel's health surface, split the way process supervisors expect it:
/// <c>/health/live</c> answers "is this process alive" (systemd restarts it when it stops
/// answering), <c>/health/ready</c> answers "may it receive traffic" (the installer and any proxy
/// wait on it), and <c>/health</c> stays the human-facing summary.
/// </summary>
public static class HealthEndpoint
{
    /// <summary>Maps the three health routes.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapPanelHealth(this IEndpointRouteBuilder endpoints)
    {
        // Liveness must never depend on a dependency: if it did, a database outage would make
        // systemd kill a perfectly healthy process and turn an outage into a restart loop.
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));

        endpoints.MapGet("/health/ready", async (AgentHealthProbe agent, DatabaseHealthProbe database) =>
        {
            var databaseStatus = await database.ProbeAsync();
            var report = new PanelHealthReport("ok", await agent.ProbeAsync(), databaseStatus);

            // The agent may legitimately be absent (not installed yet), but without the database
            // the panel cannot serve anyone — so only the database decides readiness.
            return databaseStatus == DatabaseHealthProbe.Reachable
                ? Results.Ok(report)
                : Results.Json(report, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        endpoints.MapGet("/health", async (AgentHealthProbe agent, DatabaseHealthProbe database) =>
            Results.Ok(new PanelHealthReport("ok", await agent.ProbeAsync(), await database.ProbeAsync())));

        return endpoints;
    }
}
