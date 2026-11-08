using System.Globalization;
using FluentValidation;
using Maran.Host.Authorization;
using Maran.Host.Configuration;
using Maran.Host.RateLimiting;
using Maran.Sdk.ModelBinding;
using Maran.Sdk.Streaming;
using Microsoft.Extensions.Options;
using Wolverine;

namespace Maran.Host.Dashboard;

/// <summary>
/// Publishes the live dashboard as one server-sent-events stream: <c>GET /api/v1/dashboard/stream</c>
/// (issue #199). One connection carries host readings, services, counts, attention and activity, so the
/// screen opens a single stream where it would otherwise poll five routes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Administrators only, and refused before a byte is streamed.</b> The route requires the
/// administrator policy, so a customer, a reseller, an API key and an anonymous caller are answered
/// 403 or 401 by the authorization middleware while the response is still a plain error. Only an
/// administrator ever reaches <see cref="HostDashboardStreamSource"/>, whose sections are server-wide
/// reads. The customer's own stream is a separate source (slice S3), not a flag on this one. The route
/// declares no view-as access because that attribute is for actions a customer token can reach, and an
/// administrator-only action is exempt from the declaration.
/// </para>
/// <para>
/// <b>Three bounds on how many and how long.</b> One signed-in user may hold
/// <see cref="DashboardStreamOptions.PerUserStreamLimit"/> streams
/// (<see cref="DashboardStreamRateLimitPolicy"/>, 429 beyond it), the process
/// <see cref="DashboardStreamOptions.PerProcessStreamLimit"/> (<see cref="DashboardStreamGate"/>, the
/// same 429 and the same code), and one stream lives
/// <see cref="DashboardStreamOptions.MaxLifetimeSeconds"/> before the server ends it so the client
/// reopens and the token is checked again.
/// </para>
/// <para>
/// <b><c>since</c>, the only input.</b> An ISO 8601 instant naming the earliest reading wanted in the
/// back-fill. It is parsed strictly: a value that is not an instant is the standard 400
/// <c>RequestParameterInvalid</c>, never a quiet fall back to the default. The writer sends no event ids
/// and reads no <c>Last-Event-ID</c> (<see cref="EventStreamWriter"/> says why), so this parameter is
/// how a client that lost its connection says where it stopped. A value older than the ring is clamped
/// to it by the feed.
/// </para>
/// <para>
/// UNOBSERVED HERE: the tests run on an in-process server, so behaviour behind a buffering reverse
/// proxy is not exercised; such a proxy needs an unbuffered route and a read timeout above the
/// heartbeat.
/// </para>
/// </remarks>
public static class DashboardStreamEndpoint
{
    /// <summary>
    /// The only spellings of <c>since</c> accepted: an ISO 8601 instant with or without fractional
    /// seconds, with <c>Z</c>, an offset, or none. A JavaScript <c>toISOString()</c> is the first.
    /// </summary>
    private static readonly string[] SinceFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd'T'HH:mm:ssK",
    ];

    /// <summary>The longest <c>since</c> accepted; an instant is far shorter, so more is not one.</summary>
    private const int SinceMaxLength = 64;

    /// <summary>How long a client refused for the process limit is told to wait, in seconds.</summary>
    private const int RetryAfterSeconds = 15;

    /// <summary>Maps <c>GET /api/v1/dashboard/stream</c>.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapDashboardStream(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/v1/dashboard/stream",
            async (
                HttpContext httpContext,
                HostDashboardStreamSource source,
                DashboardStreamGate gate,
                IMessageBus bus,
                IOptions<DashboardStreamOptions> options,
                ILoggerFactory loggers,
                string? since,
                CancellationToken cancellationToken) =>
            {
                // Before the process slot is taken and before any header is written: a malformed value
                // is a plain 400, which a stream that had already begun could no longer be.
                var wanted = ParseSince(since);

                using var slot = gate.TryEnter();
                if (slot is null)
                {
                    await RateLimitedProblemWriter.WriteAsync(httpContext, RetryAfterSeconds, cancellationToken);
                    return Results.Empty;
                }

                await EventStreamWriter.WriteAsync(
                    httpContext.Response,
                    source.StreamAsync(bus, loggers.CreateLogger(typeof(DashboardStreamEndpoint)), wanted, cancellationToken),
                    frame =>
                    {
                        return EventStreamFrame.Render(frame.EventName(), frame.ToPayload());
                    },
                    options.Value.Heartbeat,
                    cancellationToken);

                // The body was written by the stream; there is nothing left for a result to add.
                return Results.Empty;
            })
            .RequireAuthorization(RolePolicies.AdminOnly)
            .RequireRateLimiting(DashboardStreamRateLimitPolicy.Name);
        return endpoints;
    }

    /// <summary>Parses the <c>since</c> query value strictly.</summary>
    /// <param name="since">The raw value, or <c>null</c> when the client named none.</param>
    /// <returns>The instant, or <c>null</c> when none was named.</returns>
    /// <exception cref="ValidationException">The value is present and is not an ISO 8601 instant.</exception>
    private static DateTimeOffset? ParseSince(string? since)
    {
        if (since is null)
        {
            return null;
        }

        // A value with no offset is read as UTC and never as the server's local time: the machine's
        // zone is not something a client can know.
        if (since.Length <= SinceMaxLength
            && DateTimeOffset.TryParseExact(
                since,
                SinceFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        throw InvalidModelStateRefusal.ForParameter("since");
    }
}
