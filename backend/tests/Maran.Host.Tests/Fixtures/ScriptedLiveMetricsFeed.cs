using System.Threading.Channels;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Tests.Fixtures;

/// <summary>
/// A live-metrics feed a test drives: it serves a scripted back-fill, hands out subscriptions, and
/// delivers whatever the test publishes.
/// </summary>
public sealed class ScriptedLiveMetricsFeed : ILiveMetricsFeed
{
    /// <summary>Every live subscription.</summary>
    private readonly List<ScriptedSubscription> _subscriptions = [];

    /// <summary>What <see cref="GetHistoryAsync"/> answers.</summary>
    public List<LiveMetricsReading> History { get; } = [];

    /// <summary>The <c>since</c> of the last back-fill request, or <c>null</c> if none was made.</summary>
    public DateTimeOffset? RequestedSince { get; private set; }

    /// <summary>How many subscriptions are live.</summary>
    public int LiveSubscriptions
    {
        get
        {
            lock (_subscriptions)
            {
                return _subscriptions.Count;
            }
        }
    }

    /// <inheritdoc />
    public ILiveMetricsSubscription Subscribe()
    {
        var subscription = new ScriptedSubscription(this);
        lock (_subscriptions)
        {
            _subscriptions.Add(subscription);
        }

        return subscription;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LiveMetricsReading>> GetHistoryAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        RequestedSince = since;
        return Task.FromResult<IReadOnlyList<LiveMetricsReading>>([.. History]);
    }

    /// <summary>Delivers a reading to every live subscription.</summary>
    /// <param name="reading">The reading.</param>
    public void Publish(LiveMetricsReading reading)
    {
        ScriptedSubscription[] receivers;
        lock (_subscriptions)
        {
            receivers = [.. _subscriptions];
        }

        foreach (var receiver in receivers)
        {
            receiver.Offer(reading);
        }
    }

    /// <summary>One scripted registration.</summary>
    private sealed class ScriptedSubscription : ILiveMetricsSubscription
    {
        /// <summary>The feed to unregister from.</summary>
        private readonly ScriptedLiveMetricsFeed _feed;

        /// <summary>The readings waiting for the consumer.</summary>
        private readonly Channel<LiveMetricsReading> _channel = Channel.CreateUnbounded<LiveMetricsReading>();

        /// <summary>Creates a registration.</summary>
        /// <param name="feed">The feed to unregister from.</param>
        public ScriptedSubscription(ScriptedLiveMetricsFeed feed)
        {
            _feed = feed;
        }

        /// <inheritdoc />
        public IAsyncEnumerable<LiveMetricsReading> ReadAllAsync(CancellationToken cancellationToken)
        {
            return _channel.Reader.ReadAllAsync(cancellationToken);
        }

        /// <summary>Queues a reading for the consumer.</summary>
        /// <param name="reading">The reading.</param>
        public void Offer(LiveMetricsReading reading)
        {
            _channel.Writer.TryWrite(reading);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_feed._subscriptions)
            {
                _feed._subscriptions.Remove(this);
            }

            _channel.Writer.TryComplete();
        }
    }
}
