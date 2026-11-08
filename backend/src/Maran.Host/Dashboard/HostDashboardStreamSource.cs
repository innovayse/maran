using System.Runtime.CompilerServices;
using Maran.Host.Configuration;
using Maran.Host.Dashboard.Frames;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.ListRecentAuditEvents;
using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Options;
using Maran.Modules.Monitoring.Queries.ListServiceStatuses;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.Sdk.Streaming;
using Microsoft.Extensions.Options;
using Wolverine;

namespace Maran.Host.Dashboard;

/// <summary>
/// Produces the frames of an ADMINISTRATOR's live dashboard stream: the host readings from the shared
/// feed, and the panel's own sections re-read under the caller's request.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order of a stream.</b> <c>hello</c>, then <c>history</c> (the back-fill), then every section once,
/// then <c>metrics</c> as the feed publishes and each section again when its interval is due and its
/// content changed. The subscription to the feed is taken BEFORE the back-fill is read and readings at
/// or before the newest back-filled instant are skipped, so a reading published while the back-fill was
/// being fetched is neither lost nor sent twice.
/// </para>
/// <para>
/// <b>It ends itself.</b> After <see cref="DashboardStreamOptions.MaxLifetimeSeconds"/> the stream
/// sends <c>end</c> with reason <c>renew</c> and finishes, and the client reopens it: reopening is a new
/// authenticated request, so a token revoked in the meantime stops working at the latest then. The
/// lifetime is judged against the injected clock at every wake, never against a timer, so a test moves
/// the clock instead of waiting.
/// </para>
/// <para>
/// <b>Host readings are shared; the sections are not, and why.</b> The agent is polled once per tick by
/// the feed whatever the number of streams. The counts, attention block, services and audit entries are
/// read here, once per connection per interval, through the bus under the caller's own request —
/// because the module handlers behind them resolve their tenant context from the current caller, and a
/// shared background reader has none (a handler then answers as an anonymous caller and shows
/// nothing). The cost is bounded by the per-user and per-process stream limits and by the intervals,
/// and each read is bounded by <see cref="DashboardSectionReader.DefaultSectionTimeout"/>. A section is
/// sent only when it differs from the last one sent, so a quiet panel sends nothing but metrics.
/// </para>
/// <para>
/// Only administrators reach this type: the endpoint's policy refuses every other caller before a
/// response header is written. A customer's stream (issue #199, slice S3) is a different source built
/// beside it, which will not take the host feed at all.
/// </para>
/// </remarks>
public sealed class HostDashboardStreamSource
{
    /// <summary>The wire version announced in <c>hello</c>.</summary>
    public const int Protocol = 1;

    /// <summary>How far back a stream that names no <c>since</c> is back-filled: the ring's hour.</summary>
    private static readonly TimeSpan DefaultBackFill = TimeSpan.FromHours(1);

    /// <summary>The shared feed of host readings.</summary>
    private readonly ILiveMetricsFeed _feed;

    /// <summary>The panel's clock; the lifetime and the section intervals are judged against it.</summary>
    private readonly IClock _clock;

    /// <summary>The stream's bounds.</summary>
    private readonly DashboardStreamOptions _options;

    /// <summary>The feed's cadence, announced to the client.</summary>
    private readonly LiveMetricsOptions _liveMetrics;

    /// <summary>Tells the stream the panel is stopping.</summary>
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>Creates the source.</summary>
    /// <param name="feed">The shared feed of host readings.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="options">The stream's validated bounds.</param>
    /// <param name="liveMetrics">The feed's validated settings.</param>
    /// <param name="lifetime">The host's lifetime.</param>
    public HostDashboardStreamSource(
        ILiveMetricsFeed feed,
        IClock clock,
        IOptions<DashboardStreamOptions> options,
        IOptions<LiveMetricsOptions> liveMetrics,
        IHostApplicationLifetime lifetime)
    {
        _feed = feed;
        _clock = clock;
        _options = options.Value;
        _liveMetrics = liveMetrics.Value;
        _lifetime = lifetime;
    }

    /// <summary>Produces the stream's frames until it ends itself or the client goes away.</summary>
    /// <param name="bus">The request's bus, which the sections are read through under the caller.</param>
    /// <param name="logger">Where a section that threw is recorded.</param>
    /// <param name="since">The earliest reading wanted in the back-fill, or <c>null</c> for the last hour.</param>
    /// <param name="cancellationToken">Cancelled when the client goes away.</param>
    /// <returns>The frames, ending with one <c>end</c> frame when the server ends the stream.</returns>
    public async IAsyncEnumerable<DashboardStreamFrame> StreamAsync(
        IMessageBus bus,
        ILogger logger,
        DateTimeOffset? since,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var startedAt = _clock.UtcNow;
        var deadline = startedAt + _options.MaxLifetime;
        var sections = SectionsFor(bus, logger);

        // Before the back-fill is read, so nothing published meanwhile is missed.
        var subscription = _feed.Subscribe();
        IAsyncEnumerator<LiveMetricsReading>? readings = null;
        Task<bool>? nextReading = null;
        Task<IReadOnlyList<DashboardStreamFrame>>? refresh = null;

        try
        {
            yield return new HelloFrame(
                Protocol,
                StreamAudience.Administrator,
                _liveMetrics.IntervalMilliseconds,
                startedAt,
                (int)_options.MaxLifetime.TotalSeconds);

            var history = await _feed.GetHistoryAsync(since ?? startedAt - DefaultBackFill, cancellationToken);
            yield return new HistoryFrame([.. history.Select(LiveMetricsFrameMapper.ToFrame)]);
            var newest = history.Count > 0 ? history[^1].At : DateTimeOffset.MinValue;

            readings = subscription.ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                if (_clock.UtcNow >= deadline)
                {
                    yield return new EndFrame(StreamEndReason.Renew);
                    yield break;
                }

                if (_lifetime.ApplicationStopping.IsCancellationRequested)
                {
                    yield return new EndFrame(StreamEndReason.Shutdown);
                    yield break;
                }

                refresh ??= StartRefresh(sections, cancellationToken);
                nextReading ??= readings.MoveNextAsync().AsTask();

                using (var tickWait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    var tick = Task.Delay(_options.Tick, tickWait.Token);
                    Task[] waits = refresh is null ? [nextReading, tick] : [nextReading, refresh, tick];
                    await Task.WhenAny(waits);
                    await tickWait.CancelAsync();
                }

                if (refresh is { IsCompleted: true } finished)
                {
                    foreach (var frame in await finished)
                    {
                        yield return frame;
                    }

                    refresh = null;
                }

                if (nextReading.IsCompleted)
                {
                    if (!await nextReading)
                    {
                        yield break;
                    }

                    var reading = readings.Current;
                    nextReading = null;
                    if (reading.At > newest)
                    {
                        newest = reading.At;
                        yield return LiveMetricsFrameMapper.ToFrame(reading);
                    }
                }
            }
        }
        finally
        {
            // Disposing the registration completes its channel, which ends a move still in flight; only
            // then is it safe to dispose the enumerator that move belongs to (EventStreamWriter's
            // remarks tell the crash this ordering prevents).
            subscription.Dispose();
            await SettleAsync(nextReading);
            await SettleAsync(refresh);
            if (readings is not null)
            {
                await readings.DisposeAsync();
            }
        }
    }

    /// <summary>The sections a stream refreshes, each with its own interval and its last content.</summary>
    /// <param name="bus">The request's bus.</param>
    /// <param name="logger">Where a section that threw is recorded.</param>
    /// <returns>The services, the totals and the activity, all due at once so the first pass sends each.</returns>
    private List<RefreshingSection> SectionsFor(IMessageBus bus, ILogger logger)
    {
        var first = _clock.UtcNow;

        return
        [
            new RefreshingSection(_options.ServicesInterval, first, async cancellationToken =>
            {
                return new ServicesFrame(await DashboardSectionReader.ReadAsync<IReadOnlyList<ServiceStatusDto>>(
                    bus, logger, new ListServiceStatusesQuery(), cancellationToken));
            }),
            new RefreshingSection(_options.TotalsInterval, first, async cancellationToken =>
            {
                var counts = DashboardCountsReader.ReadAsync(bus, logger, cancellationToken);
                var attention = AttentionReader.ReadAsync(bus, logger, _clock, cancellationToken);
                await Task.WhenAll(counts, attention);
                return new TotalsFrame(await counts, await attention);
            }),
            new RefreshingSection(_options.ActivityInterval, first, async cancellationToken =>
            {
                return new ActivityFrame(await DashboardSectionReader.ReadAsync<IReadOnlyList<AuditEventDto>>(
                    bus,
                    logger,
                    new ListRecentAuditEventsQuery(DashboardEndpoint.RecentAuditEntries),
                    cancellationToken));
            }),
        ];
    }

    /// <summary>Reads every section that is due, together, and answers with those whose content changed.</summary>
    /// <param name="sections">The stream's sections.</param>
    /// <param name="cancellationToken">Cancelled when the client goes away.</param>
    /// <returns>
    /// A task for the changed sections' frames, or <c>null</c> when nothing is due. The stream keeps one
    /// refresh in flight at most, so a slow section delays the next pass and never stacks passes.
    /// </returns>
    private Task<IReadOnlyList<DashboardStreamFrame>>? StartRefresh(
        List<RefreshingSection> sections,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var due = sections.Where(section =>
        {
            return section.IsDue(now);
        }).ToList();

        if (due.Count == 0)
        {
            return null;
        }

        foreach (var section in due)
        {
            section.Scheduled(now);
        }

        return ReadChangedAsync(due, cancellationToken);
    }

    /// <summary>Reads the due sections together and keeps the frames of those that changed.</summary>
    /// <param name="due">The sections to read.</param>
    /// <param name="cancellationToken">Cancelled when the client goes away.</param>
    /// <returns>The changed sections' frames.</returns>
    private static async Task<IReadOnlyList<DashboardStreamFrame>> ReadChangedAsync(
        List<RefreshingSection> due,
        CancellationToken cancellationToken)
    {
        var frames = await Task.WhenAll(due.Select(section =>
        {
            return section.ReadIfChangedAsync(cancellationToken);
        }));

        return [.. frames.OfType<DashboardStreamFrame>()];
    }

    /// <summary>Waits out a task the stream started, ignoring only its cancellation.</summary>
    /// <param name="pending">The task still in flight, or <c>null</c>.</param>
    /// <returns>Resolves when it has finished.</returns>
    private static async Task SettleAsync(Task? pending)
    {
        if (pending is null)
        {
            return;
        }

        try
        {
            await pending;
        }
        catch (OperationCanceledException)
        {
            // The stream is ending because it was cancelled; that is what cancelled this.
        }
    }

    /// <summary>One section of the stream: how to read it, how often, and what it last sent.</summary>
    private sealed class RefreshingSection
    {
        /// <summary>How often the section is re-read.</summary>
        private readonly TimeSpan _interval;

        /// <summary>Reads the section and builds its frame.</summary>
        private readonly Func<CancellationToken, Task<DashboardStreamFrame>> _read;

        /// <summary>When the section is next due.</summary>
        private DateTimeOffset _due;

        /// <summary>The wire text of the last frame sent, to tell a change from a repeat.</summary>
        private string? _lastSent;

        /// <summary>Creates a section.</summary>
        /// <param name="interval">How often it is re-read.</param>
        /// <param name="due">When it is first due.</param>
        /// <param name="read">Reads it and builds its frame.</param>
        public RefreshingSection(
            TimeSpan interval,
            DateTimeOffset due,
            Func<CancellationToken, Task<DashboardStreamFrame>> read)
        {
            _interval = interval;
            _due = due;
            _read = read;
        }

        /// <summary>Whether the section should be read now.</summary>
        /// <param name="now">The panel's clock.</param>
        /// <returns>True when its interval has elapsed.</returns>
        public bool IsDue(DateTimeOffset now)
        {
            return now >= _due;
        }

        /// <summary>Records that a read is starting, so the next one is an interval away.</summary>
        /// <param name="now">The panel's clock.</param>
        public void Scheduled(DateTimeOffset now)
        {
            _due = now + _interval;
        }

        /// <summary>Reads the section and answers with its frame only when it differs from the last one sent.</summary>
        /// <param name="cancellationToken">Cancelled when the client goes away.</param>
        /// <returns>The frame, or <c>null</c> when nothing changed.</returns>
        public async Task<DashboardStreamFrame?> ReadIfChangedAsync(CancellationToken cancellationToken)
        {
            var frame = await _read(cancellationToken);
            var wire = EventStreamFrame.Render(frame.EventName(), frame.ToPayload());
            if (wire == _lastSent)
            {
                return null;
            }

            _lastSent = wire;
            return frame;
        }
    }
}
