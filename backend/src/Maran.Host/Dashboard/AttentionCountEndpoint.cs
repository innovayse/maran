using Maran.Host.Authorization;
using Maran.Host.RateLimiting;
using Wolverine;

namespace Maran.Host.Dashboard;

/// <summary>
/// Publishes the one figure the navigation badge needs — how many things on this server ask to be
/// looked at — without the rest of the landing page.
/// </summary>
/// <remarks>
/// <para>
/// The shell asks on every screen, so it must not pay for the whole dashboard each time: this route
/// reads only the attention sources, through the same <see cref="AttentionReader"/> the dashboard
/// uses, and adds them with <see cref="AttentionTally"/>. The answer is a
/// <see cref="DashboardSection{T}"/> — loaded with the count, or unavailable — so a figure that could
/// not be taken is never a zero.
/// </para>
/// <para>
/// <b>Administrators only, refused at the route.</b> The sources are server-wide and the dashboard
/// withholds them from everyone else; here the withholding is a 403 by policy, since there is no
/// other part of this answer a customer could be given. The check precedes every dispatch.
/// </para>
/// </remarks>
public static class AttentionCountEndpoint
{
    /// <summary>Maps <c>GET /api/v1/dashboard/attention-count</c>.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapAttentionCount(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/v1/dashboard/attention-count",
            async (
                IMessageBus bus,
                IClock clock,
                ILoggerFactory loggers,
                CancellationToken cancellationToken) =>
            {
                var logger = loggers.CreateLogger(typeof(AttentionCountEndpoint));
                var attention = await AttentionReader.ReadAsync(bus, logger, clock, cancellationToken);
                return Results.Ok(AttentionTally.Total(attention));
            })
            .RequireAuthorization(RolePolicies.AdminOnly)
            .RequireRateLimiting(ApiRateLimitPolicy.Name)
            .Produces<DashboardSection<int>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
        return endpoints;
    }
}
