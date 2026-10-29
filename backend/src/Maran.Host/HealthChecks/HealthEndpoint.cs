namespace Maran.Host.HealthChecks;

/// <summary>
/// The panel's health surface, in three routes: <c>/health/live</c> answers "is this process
/// serving", <c>/health/ready</c> answers "may it receive traffic", and <c>/health</c> stays the
/// human-facing summary.
/// </summary>
/// <remarks>
/// Who reads them, so nobody designs against a reader that does not exist: no shipped supervisor
/// polls any of the three. <c>maran-api.service</c> restarts the process only when it EXITS
/// (<c>Restart=on-failure</c>, no watchdog), and the installer waits on the api's and the agent's
/// sockets, not on a route (<c>installer/lib/70-services.sh</c>), and tells the operator to ask
/// <c>/health</c> when it finishes. <c>/health</c> is also what the installer polygon
/// (<c>scripts/lib/install-polygon.sh</c>), <c>maran dev</c> and the <c>maran handshake</c> gate read,
/// by value; readiness is for a proxy or monitor an operator puts in front of the panel. That is also
/// why a readiness that fails cannot cause a restart loop: nothing restarts the panel for it.
/// </remarks>
public static class HealthEndpoint
{
    /// <summary>Maps the three health routes.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapPanelHealth(this IEndpointRouteBuilder endpoints)
    {
        // Liveness must never depend on a dependency: anything that restarts the panel on its answer
        // would otherwise turn a database outage into a restart loop of a perfectly healthy process.
        // Anonymous, all three: a monitor or proxy that probes the panel holds no panel credential.
        // The reports carry three words and no version, path or host detail. The one thing they tell
        // an anonymous caller beyond "the panel answers" is the state of the agent and the database —
        // including `incompatible`, which says an upgrade was left half-done without saying which
        // revisions. That is accepted: readiness answering 503 already says something is wrong, and
        // an operator diagnosing it must not need a login to read why.
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

        endpoints.MapGet("/health/ready", async (AgentHealthProbe agent, DatabaseHealthProbe database) =>
        {
            var databaseStatus = await database.ProbeAsync();
            var agentStatus = await agent.ProbeAsync();
            var report = new PanelHealthReport("ok", agentStatus, databaseStatus);

            // Two things refuse traffic. Without the database the panel cannot serve anyone. An agent
            // that ANSWERED in a contract outside the window the panel drives means every agent action
            // sent now would be driving an agent whose answers the panel may misread. An ABSENT agent
            // does not refuse traffic: it may not be installed yet or be restarting, the panel serves
            // its own pages meanwhile, and each agent action reports the outage itself. Both answers
            // are asked afresh on every request and never cached, so finishing an upgrade turns this
            // back to 200 without restarting the API.
            var ready = databaseStatus == DatabaseHealthProbe.Reachable
                && agentStatus != AgentHealthProbe.Incompatible;

            return ready
                ? Results.Ok(report)
                : Results.Json(report, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).AllowAnonymous();

        endpoints.MapGet("/health", async (AgentHealthProbe agent, DatabaseHealthProbe database) =>
        {
            return Results.Ok(new PanelHealthReport("ok", await agent.ProbeAsync(), await database.ProbeAsync()));
        }).AllowAnonymous();

        return endpoints;
    }
}
