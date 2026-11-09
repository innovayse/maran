using Maran.Host.Authorization;
using Maran.Host.RateLimiting;
using Wolverine;

namespace Maran.Host.Dashboard;

/// <summary>
/// Publishes the first-run checklist: the steps a new administrator still has to take, derived from
/// what the panel holds.
/// </summary>
/// <remarks>
/// <para>
/// Lives in the Host because it is composition, like the attention block: it reads counts that five
/// modules already publish and assembles one answer through <see cref="FirstRunReader"/>.
/// </para>
/// <para>
/// <b>Administrators only, refused at the route.</b> The counts are server-wide and are dispatched on
/// a bus that carries no policy of its own, so the refusal is a 403 by policy before any dispatch. The
/// route is a read and writes nothing, so there is no audit entry.
/// </para>
/// </remarks>
public static class FirstRunEndpoint
{
    /// <summary>Maps <c>GET /api/v1/dashboard/first-run</c>.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapFirstRun(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/v1/dashboard/first-run",
            async (
                IMessageBus bus,
                ILoggerFactory loggers,
                CancellationToken cancellationToken) =>
            {
                var logger = loggers.CreateLogger(typeof(FirstRunEndpoint));
                return Results.Ok(await FirstRunReader.ReadAsync(bus, logger, cancellationToken));
            })
            .RequireAuthorization(RolePolicies.AdminOnly)
            .RequireRateLimiting(ApiRateLimitPolicy.Name)
            .Produces<FirstRunChecklistDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
        return endpoints;
    }
}
