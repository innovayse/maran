using Maran.Agent.Client.Services.MonitorService;
using Maran.Host.Configuration;
using Maran.Host.Dashboard;
using Maran.Host.Dashboard.Frames;
using Maran.Host.Tests.Fixtures;
using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Options;
using Maran.Modules.Monitoring.Queries.ListServiceStatuses;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Host.Tests.Dashboard;

/// <summary>
/// The administrator's stream as a sequence of frames: its order, its back-fill without repeats, its
/// sections sent only when they change, and the two ways the server ends it.
/// </summary>
/// <remarks>
/// The clock is a hand-moved one, so the lifetime and the section intervals are crossed by moving it and
/// not by waiting. The stream does wake on a real tenth of a second to notice, so a test that moves the
/// clock reads frames under a deadline token and a failure to arrive is a failed test and not a hang.
/// UNOBSERVED HERE: the HTTP framing and the process-wide caps are the endpoint's, covered over HTTP.
/// </remarks>
public sealed class HostDashboardStreamSourceTests
{
    /// <summary>The bound on every wait for a frame.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    /// <summary>A stream opens with hello and the back-fill, then sends each section once.</summary>
    [Fact]
    public async Task A_stream_opens_with_hello_and_the_back_fill_then_sends_each_section_once()
    {
        using var world = new World();
        world.Feed.History.Add(Reading(world.Clock.UtcNow.AddSeconds(-2), 1));

        var frames = await world.ReadUntilAsync(read =>
        {
            return read.OfType<ActivityFrame>().Any();
        });

        var hello = Assert.IsType<HelloFrame>(frames[0]);
        Assert.Equal(HostDashboardStreamSource.Protocol, hello.Protocol);
        Assert.Equal(StreamAudience.Administrator, hello.Audience);
        Assert.Equal(900, hello.MaxLifetimeSec);
        var history = Assert.IsType<HistoryFrame>(frames[1]);
        Assert.Equal(1d, Assert.Single(history.Points).Cpu.Total);
        Assert.Single(frames.OfType<ServicesFrame>());
        Assert.Single(frames.OfType<TotalsFrame>());
        Assert.Single(frames.OfType<ActivityFrame>());
    }

    /// <summary>A stream with no since is back-filled from an hour ago, and one with a since from that instant.</summary>
    [Fact]
    public async Task A_stream_with_no_since_is_back_filled_from_an_hour_ago_and_one_with_a_since_from_that_instant()
    {
        using var plain = new World();
        await plain.ReadUntilAsync(read =>
        {
            return read.OfType<HistoryFrame>().Any();
        });
        Assert.Equal(plain.Clock.UtcNow.AddHours(-1), plain.Feed.RequestedSince);

        using var named = new World();
        var since = named.Clock.UtcNow.AddMinutes(-5);
        await named.ReadUntilAsync(
            read =>
            {
                return read.OfType<HistoryFrame>().Any();
            },
            since);
        Assert.Equal(since, named.Feed.RequestedSince);
    }

    /// <summary>A reading already in the back-fill is not sent again as a live one, and a newer reading is.</summary>
    [Fact]
    public async Task A_reading_already_in_the_back_fill_is_not_sent_again_and_a_newer_one_is()
    {
        using var world = new World();
        var old = Reading(world.Clock.UtcNow.AddSeconds(-2), 1);
        world.Feed.History.Add(old);

        await world.ReadUntilAsync(read =>
        {
            return read.OfType<ActivityFrame>().Any();
        });
        world.Feed.Publish(old);
        world.Feed.Publish(Reading(world.Clock.UtcNow, 2));
        var frames = await world.ReadUntilAsync(read =>
        {
            return read.OfType<MetricsFrame>().Any();
        });

        var live = Assert.Single(frames.OfType<MetricsFrame>());
        Assert.Equal(2d, live.Cpu.Total);
    }

    /// <summary>A stream ends itself with a renew reason once its lifetime has passed, and not before.</summary>
    [Fact]
    public async Task A_stream_ends_itself_with_a_renew_reason_once_its_lifetime_has_passed_and_not_before()
    {
        using var world = new World();
        var before = await world.ReadUntilAsync(read =>
        {
            return read.OfType<ActivityFrame>().Any();
        });
        Assert.Empty(before.OfType<EndFrame>());

        world.Clock.Advance(TimeSpan.FromSeconds(901));
        var after = await world.ReadUntilAsync(read =>
        {
            return read.OfType<EndFrame>().Any();
        });

        Assert.Equal(StreamEndReason.Renew, after.OfType<EndFrame>().Single().Reason);
        Assert.True(await world.EndedAsync());
    }

    /// <summary>A stream ends with a shutdown reason when the panel is stopping.</summary>
    [Fact]
    public async Task A_stream_ends_with_a_shutdown_reason_when_the_panel_is_stopping()
    {
        using var world = new World();
        await world.ReadUntilAsync(read =>
        {
            return read.OfType<ActivityFrame>().Any();
        });

        world.Lifetime.Stop();
        var frames = await world.ReadUntilAsync(read =>
        {
            return read.OfType<EndFrame>().Any();
        });

        Assert.Equal(StreamEndReason.Shutdown, frames.OfType<EndFrame>().Single().Reason);
    }

    /// <summary>A section whose content has not changed is read again but not sent again.</summary>
    [Fact]
    public async Task A_section_whose_content_has_not_changed_is_read_again_but_not_sent_again()
    {
        using var world = new World();
        await world.ReadUntilAsync(read =>
        {
            return read.OfType<ActivityFrame>().Any();
        });
        var firstReads = world.ServiceReads;

        // Every section is due after 31 seconds, so the stream starts a refresh on its next pass. The
        // readings keep it moving: it is pulled, and nothing runs between the reads of a test.
        world.Clock.Advance(TimeSpan.FromSeconds(31));
        world.Feed.Publish(Reading(world.Clock.UtcNow, 3));
        world.Feed.Publish(Reading(world.Clock.UtcNow.AddSeconds(2), 4));
        var frames = await world.ReadUntilAsync(read =>
        {
            return read.OfType<MetricsFrame>().Count() >= 2;
        });

        Assert.True(world.ServiceReads > firstReads, "the statuses were not asked for again");
        Assert.Single(frames.OfType<ServicesFrame>());
    }

    /// <summary>A section whose content has changed is sent again.</summary>
    [Fact]
    public async Task A_section_whose_content_has_changed_is_sent_again()
    {
        using var world = new World();
        await world.ReadUntilAsync(read =>
        {
            return read.OfType<ActivityFrame>().Any();
        });

        world.Services = [new ServiceStatusDto(AgentManagedService.WebServer, "Web server", AgentServiceState.Stopped, "inactive")];
        world.Clock.Advance(TimeSpan.FromSeconds(31));
        var frames = await world.ReadUntilAsync(read =>
        {
            return read.OfType<ServicesFrame>().Count() >= 2;
        });

        Assert.Equal(2, frames.OfType<ServicesFrame>().Count());
    }

    /// <summary>Ending the stream releases its registration with the feed.</summary>
    [Fact]
    public async Task Ending_the_stream_releases_its_registration_with_the_feed()
    {
        using var world = new World();
        await world.ReadUntilAsync(read =>
        {
            return read.OfType<ActivityFrame>().Any();
        });
        Assert.Equal(1, world.Feed.LiveSubscriptions);

        await world.DisposeEnumeratorAsync();

        Assert.Equal(0, world.Feed.LiveSubscriptions);
    }

    /// <summary>One reading at an instant, with a processor figure that tells readings apart.</summary>
    /// <param name="at">When it was taken.</param>
    /// <param name="cpu">The processor utilisation.</param>
    /// <returns>The reading.</returns>
    private static LiveMetricsReading Reading(DateTimeOffset at, double cpu)
    {
        return new LiveMetricsReading(
            at,
            new LiveCpuReading(cpu, 0.5, 0.4, 0.3),
            new LiveMemoryReading(1, 2),
            new LiveDiskReading(1, 2),
            new LiveNetworkReading(1, 2));
    }

    /// <summary>One stream under test with everything it depends on, and the helpers that drive it.</summary>
    private sealed class World : IDisposable
    {
        /// <summary>The frames read off the stream so far.</summary>
        private readonly List<DashboardStreamFrame> _frames = [];

        /// <summary>Cancels every wait after the test deadline.</summary>
        private readonly CancellationTokenSource _deadline = new(Deadline);

        /// <summary>The source under test.</summary>
        private readonly HostDashboardStreamSource _source;

        /// <summary>The bus the sections are read through.</summary>
        private readonly ScriptedMessageBus _bus;

        /// <summary>The running enumeration, started by the first read.</summary>
        private IAsyncEnumerator<DashboardStreamFrame>? _enumerator;

        /// <summary>How many times the service statuses were asked for.</summary>
        private int _serviceReads;

        /// <summary>The clock the lifetime and the intervals are judged against.</summary>
        public MovableClock Clock { get; } = new();

        /// <summary>The feed the stream reads host readings from.</summary>
        public ScriptedLiveMetricsFeed Feed { get; } = new();

        /// <summary>The host's lifetime, which a test stops.</summary>
        public StoppableLifetime Lifetime { get; } = new();

        /// <summary>What the service statuses query answers.</summary>
        public IReadOnlyList<ServiceStatusDto> Services { get; set; } =
            [new ServiceStatusDto(AgentManagedService.WebServer, "Web server", AgentServiceState.Running, "active")];

        /// <summary>How many times the service statuses were asked for.</summary>
        public int ServiceReads
        {
            get
            {
                return Volatile.Read(ref _serviceReads);
            }
        }

        /// <summary>Builds the source over scripted collaborators.</summary>
        public World()
        {
            // Only the service statuses answer; every other query throws, which the section reader turns
            // into an unavailable section. That is enough to observe ORDER, change detection and ending.
            _bus = new ScriptedMessageBus
            {
                Responder = (message, _) =>
                {
                    if (message is ListServiceStatusesQuery)
                    {
                        Interlocked.Increment(ref _serviceReads);
                        return Task.FromResult<object>(Result<IReadOnlyList<ServiceStatusDto>>.Ok(Services));
                    }

                    throw new InvalidOperationException("not scripted");
                },
            };

            _source = new HostDashboardStreamSource(
                Feed,
                Clock,
                new OptionsWrapper<DashboardStreamOptions>(new DashboardStreamOptions { TickMilliseconds = 100 }),
                new OptionsWrapper<LiveMetricsOptions>(new LiveMetricsOptions()),
                Lifetime);
        }

        /// <summary>Reads frames until <paramref name="done"/> accepts all those read so far.</summary>
        /// <param name="done">Judges the frames read so far; true stops the read.</param>
        /// <param name="since">The since to open the stream with, on the first read.</param>
        /// <returns>Every frame read since the stream opened.</returns>
        public async Task<IReadOnlyList<DashboardStreamFrame>> ReadUntilAsync(
            Func<IReadOnlyList<DashboardStreamFrame>, bool> done,
            DateTimeOffset? since = null)
        {
            _enumerator ??= _source
                .StreamAsync(_bus, NullLogger.Instance, since, _deadline.Token)
                .GetAsyncEnumerator(_deadline.Token);

            while (!done(_frames))
            {
                if (!await _enumerator.MoveNextAsync())
                {
                    return _frames;
                }

                _frames.Add(_enumerator.Current);
            }

            return _frames;
        }

        /// <summary>Whether the stream has finished: the next move finds nothing more.</summary>
        /// <returns>True when the enumeration is over.</returns>
        public async Task<bool> EndedAsync()
        {
            return !await _enumerator!.MoveNextAsync();
        }

        /// <summary>Disposes the enumeration, as the writer does when the client goes away.</summary>
        /// <returns>Resolves when its cleanup has run.</returns>
        public async Task DisposeEnumeratorAsync()
        {
            await _enumerator!.DisposeAsync();
            _enumerator = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _deadline.Cancel();
            _deadline.Dispose();
            Lifetime.Dispose();
        }
    }
}
