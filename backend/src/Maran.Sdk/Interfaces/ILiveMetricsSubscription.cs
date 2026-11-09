using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// One consumer's registration with <see cref="ILiveMetricsFeed"/>: it exists from the moment it is
/// created, so a reading published while the consumer is still fetching its back-fill is not missed.
/// </summary>
/// <remarks>
/// Disposing it unregisters the consumer. A feed with no registered consumer stops polling the agent
/// after its idle period, which is why a consumer MUST dispose it.
/// </remarks>
public interface ILiveMetricsSubscription : IDisposable
{
    /// <summary>
    /// The readings published after this subscription was created, newest last. Bounded: a consumer that
    /// reads slower than the feed publishes sees the latest readings and loses the intermediate ones,
    /// and nothing is buffered without bound on its behalf.
    /// </summary>
    /// <param name="cancellationToken">Ends the enumeration.</param>
    /// <returns>The readings, until the subscription is disposed or the token is cancelled.</returns>
    IAsyncEnumerable<LiveMetricsReading> ReadAllAsync(CancellationToken cancellationToken);
}
