using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// The shared, in-memory feed of host readings behind the live dashboard: one poll of the agent per
/// tick however many consumers are watching, a ring of the last hour for a consumer that reconnects.
/// </summary>
/// <remarks>
/// A read-only window implemented by the Monitoring module (rules/architecture.md "A shared
/// facility's contract lives in <c>Maran.Sdk</c>"); the Host composes a stream from it and holds no
/// metrics logic. The feed is demand-driven: it polls the agent only while at least one subscription
/// exists, so a panel nobody is watching costs the agent nothing.
/// </remarks>
public interface ILiveMetricsFeed
{
    /// <summary>
    /// Registers a consumer. Dispose the result when done; the feed stops polling after its idle
    /// period once none is left.
    /// </summary>
    /// <returns>The registration, already live.</returns>
    ILiveMetricsSubscription Subscribe();

    /// <summary>Returns the readings from <paramref name="since"/> onward, oldest first.</summary>
    /// <param name="since">
    /// The earliest instant wanted; anything older than the ring's reach (one hour) is clamped to it.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The in-memory ring's readings, preceded by stored one-minute samples for the part of the window
    /// the ring does not yet hold (a cold start); the older points are simply coarser, and the answer
    /// does not say so. Capped at the ring's capacity.
    /// </returns>
    Task<IReadOnlyList<LiveMetricsReading>> GetHistoryAsync(DateTimeOffset since, CancellationToken cancellationToken);
}
