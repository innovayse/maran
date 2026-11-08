using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// <c>GET /api/v1/dashboard/stream</c> over real HTTP: who may open the live dashboard, what it sends
/// first, how it back-fills a reconnecting client, and the bounds on how many and how long.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every refusing assertion has an inverse control.</b> A stream refused to everybody would pass a
/// test that only hands it a customer, so the administrator who must be admitted is asserted in the same
/// test as the caller who must not be.
/// </para>
/// <para>
/// UNOBSERVED HERE: these run on an in-process server, which has neither Kestrel's output buffering nor
/// a proxy in front of it, so they cannot tell a stream that flushes from one that a proxy would hold
/// back (<c>EventStreamWriter</c> states the same limit). The reseller and API-key refusals are the
/// authorization policy's and are covered by the endpoint census that walks every route; this class
/// pins the two callers that matter for the tenant boundary, the customer and the anonymous one.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class DashboardStreamEndpointTests : IAsyncLifetime
{
    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public DashboardStreamEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>An anonymous caller and a customer are refused before anything is streamed, and an administrator is admitted.</summary>
    [Fact]
    public async Task An_anonymous_caller_and_a_customer_are_refused_and_an_administrator_is_admitted()
    {
        await using var factory = DashboardStreamHost.CreateFactory(_pg, new StubAgentMonitorClient());
        await DashboardStreamHost.PrepareAsync(factory);
        using var anonymous = factory.CreateClient();
        using var customer = await EndpointHarness.SignInAsync(factory, DashboardStreamHost.Customer);
        using var administrator = await EndpointHarness.SignInAsync(factory, "admin");
        using var deadline = new CancellationTokenSource(DashboardStreamHost.ReadTimeout);

        using var anonymousResponse = await DashboardStreamHost.OpenAsync(anonymous, string.Empty, deadline.Token);
        using var customerResponse = await DashboardStreamHost.OpenAsync(customer, string.Empty, deadline.Token);
        using var adminResponse = await DashboardStreamHost.OpenAsync(administrator, string.Empty, deadline.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, customerResponse.StatusCode);
        Assert.NotEqual("text/event-stream", customerResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        await deadline.CancelAsync();
    }

    /// <summary>A stream opens with hello and the back-fill, then sends every section and the live readings.</summary>
    [Fact]
    public async Task A_stream_opens_with_hello_and_the_back_fill_then_sends_every_section_and_live_readings()
    {
        await using var factory = DashboardStreamHost.CreateFactory(_pg, new StubAgentMonitorClient());
        await DashboardStreamHost.PrepareAsync(factory);
        using var administrator = await EndpointHarness.SignInAsync(factory, "admin");
        using var deadline = new CancellationTokenSource(DashboardStreamHost.ReadTimeout);

        using var response = await DashboardStreamHost.OpenAsync(administrator, string.Empty, deadline.Token);
        var events = await DashboardStreamHost.ReadUntilAsync(
            response,
            read =>
            {
                return Names(read).Contains("metrics")
                    && Names(read).Contains("services")
                    && Names(read).Contains("totals")
                    && Names(read).Contains("activity");
            },
            deadline.Token);

        Assert.Equal("hello", events[0].Name);
        Assert.Equal(1, events[0].Payload.GetProperty("protocol").GetInt32());
        Assert.Equal("administrator", events[0].Payload.GetProperty("audience").GetString());
        Assert.Equal(900, events[0].Payload.GetProperty("maxLifetimeSec").GetInt32());
        Assert.Equal("history", events[1].Name);

        var metrics = events.First(read =>
        {
            return read.Name == "metrics";
        }).Payload;
        Assert.Equal(7.5, metrics.GetProperty("cpu").GetProperty("total").GetDouble());
        Assert.Equal(
            [0.4, 0.3, 0.2],
            metrics.GetProperty("cpu").GetProperty("load").EnumerateArray().Select(load =>
            {
                return load.GetDouble();
            }));
        Assert.Equal(2_048, metrics.GetProperty("memory").GetProperty("used").GetInt64());
        Assert.Equal(40, metrics.GetProperty("disk").GetProperty("used").GetInt64());

        // The first reading has no rate: the member is null, not zero. Which frame carries it depends on
        // whether the feed's first poll landed before the back-fill was read (then it is the back-fill's
        // only point) or after (then it is the first live reading), so the earliest reading the client
        // received, from either, is the one judged.
        var received = events
            .Where(read => { return read.Name == "history"; })
            .SelectMany(read => { return read.Payload.GetProperty("points").EnumerateArray(); })
            .Concat(events.Where(read => { return read.Name == "metrics"; }).Select(read => { return read.Payload; }))
            .OrderBy(point => { return point.GetProperty("t").GetDateTimeOffset(); })
            .ToList();
        Assert.Equal(JsonValueKind.Null, received[0].GetProperty("network").GetProperty("rxBps").ValueKind);

        var services = events.First(read =>
        {
            return read.Name == "services";
        }).Payload.GetProperty("services");
        Assert.Equal("loaded", services.GetProperty("state").GetString());
        Assert.Equal(2, services.GetProperty("data").GetArrayLength());

        var counts = events.First(read =>
        {
            return read.Name == "totals";
        }).Payload.GetProperty("counts").GetProperty("accounts");
        Assert.Equal("loaded", counts.GetProperty("state").GetString());
        Assert.Equal(1, counts.GetProperty("data").GetInt32());
        await deadline.CancelAsync();
    }

    /// <summary>A client that reconnects is back-filled with the readings the feed already holds.</summary>
    [Fact]
    public async Task A_client_that_reconnects_is_back_filled_with_the_readings_the_feed_already_holds()
    {
        await using var factory = DashboardStreamHost.CreateFactory(_pg, new StubAgentMonitorClient());
        await DashboardStreamHost.PrepareAsync(factory);
        using var administrator = await EndpointHarness.SignInAsync(factory, "admin");
        using var deadline = new CancellationTokenSource(DashboardStreamHost.ReadTimeout);

        // The first stream fills the ring with at least one reading, and the ring outlives the stream.
        using var first = await DashboardStreamHost.OpenAsync(administrator, string.Empty, deadline.Token);
        await DashboardStreamHost.ReadUntilAsync(
            first,
            read =>
            {
                return Names(read).Contains("metrics");
            },
            deadline.Token);

        var since = factory.Services.GetRequiredService<IClock>().UtcNow.AddHours(-1).ToString("o");
        using var second = await DashboardStreamHost.OpenAsync(administrator, $"?since={Uri.EscapeDataString(since)}", deadline.Token);
        var events = await DashboardStreamHost.ReadUntilAsync(
            second,
            read =>
            {
                return Names(read).Contains("history");
            },
            deadline.Token);

        var points = events.First(read =>
        {
            return read.Name == "history";
        }).Payload.GetProperty("points");
        Assert.True(points.GetArrayLength() >= 1);
        Assert.Equal(7.5, points[0].GetProperty("cpu").GetProperty("total").GetDouble());
        await deadline.CancelAsync();
    }

    /// <summary>A malformed since is refused with the standard 400, and a well-formed one is accepted.</summary>
    [Fact]
    public async Task A_malformed_since_is_refused_with_the_standard_400_and_a_well_formed_one_is_accepted()
    {
        await using var factory = DashboardStreamHost.CreateFactory(_pg, new StubAgentMonitorClient());
        await DashboardStreamHost.PrepareAsync(factory);
        using var administrator = await EndpointHarness.SignInAsync(factory, "admin");
        using var deadline = new CancellationTokenSource(DashboardStreamHost.ReadTimeout);

        using var malformed = await DashboardStreamHost.OpenAsync(administrator, "?since=yesterday", deadline.Token);
        using var wellFormed = await DashboardStreamHost.OpenAsync(
            administrator, "?since=2026-11-07T10%3A00%3A00.000Z", deadline.Token);

        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("RequestParameterInvalid", await ProblemDocument.CodeAsync(malformed));
        Assert.Equal(HttpStatusCode.OK, wellFormed.StatusCode);
        await deadline.CancelAsync();
    }

    /// <summary>One user may not hold more streams open than the limit, and another user is not affected.</summary>
    [Fact]
    public async Task One_user_may_not_hold_more_streams_open_than_the_limit_and_another_user_is_not_affected()
    {
        await using var factory = DashboardStreamHost.CreateFactory(
            _pg, new StubAgentMonitorClient(), configure: options =>
            {
                options.PerUserStreamLimit = 1;
            });
        await DashboardStreamHost.PrepareAsync(factory);
        using var first = await EndpointHarness.SignInAsync(factory, "admin");
        using var second = await EndpointHarness.SignInAsync(factory, DashboardStreamHost.SecondAdministrator);
        using var deadline = new CancellationTokenSource(DashboardStreamHost.ReadTimeout);

        using var held = await DashboardStreamHost.OpenAsync(first, string.Empty, deadline.Token);
        using var refused = await DashboardStreamHost.OpenAsync(first, string.Empty, deadline.Token);
        using var neighbour = await DashboardStreamHost.OpenAsync(second, string.Empty, deadline.Token);

        Assert.Equal(HttpStatusCode.OK, held.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.NotNull(refused.Headers.RetryAfter);
        Assert.Equal(HttpStatusCode.OK, neighbour.StatusCode);
        await deadline.CancelAsync();
    }

    /// <summary>The whole process may not hold more streams than its limit, whoever opens them.</summary>
    [Fact]
    public async Task The_whole_process_may_not_hold_more_streams_than_its_limit_whoever_opens_them()
    {
        await using var factory = DashboardStreamHost.CreateFactory(
            _pg, new StubAgentMonitorClient(), configure: options =>
            {
                options.PerProcessStreamLimit = 1;
            });
        await DashboardStreamHost.PrepareAsync(factory);
        using var first = await EndpointHarness.SignInAsync(factory, "admin");
        using var second = await EndpointHarness.SignInAsync(factory, DashboardStreamHost.SecondAdministrator);
        using var deadline = new CancellationTokenSource(DashboardStreamHost.ReadTimeout);

        using var held = await DashboardStreamHost.OpenAsync(first, string.Empty, deadline.Token);
        using var refused = await DashboardStreamHost.OpenAsync(second, string.Empty, deadline.Token);

        Assert.Equal(HttpStatusCode.OK, held.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("HostRateLimited", await ProblemDocument.CodeAsync(refused));
        Assert.NotNull(refused.Headers.RetryAfter);
        await deadline.CancelAsync();
    }

    /// <summary>A stream ends itself at its maximum lifetime with a renew reason, and not before.</summary>
    [Fact]
    public async Task A_stream_ends_itself_at_its_maximum_lifetime_with_a_renew_reason_and_not_before()
    {
        var clock = new AdvancingClock();
        await using var factory = DashboardStreamHost.CreateFactory(
            _pg, new StubAgentMonitorClient(), clock, options =>
            {
                options.MaxLifetimeSeconds = 30;
            });
        await DashboardStreamHost.PrepareAsync(factory);
        using var administrator = await EndpointHarness.SignInAsync(factory, "admin");
        using var deadline = new CancellationTokenSource(DashboardStreamHost.ReadTimeout);

        using var response = await DashboardStreamHost.OpenAsync(administrator, string.Empty, deadline.Token);
        using var reader = await DashboardSseReader.OpenAsync(response, deadline.Token);

        // The back-fill is what proves the stream is open and inside its lifetime. A live reading is not
        // waited for: this clock stands still until the test moves it, so every reading carries one
        // instant, and the stream sends a live reading only when it is newer than the back-fill's.
        var before = await reader.ReadUntilAsync(
            read =>
            {
                return Names(read).Contains("history");
            },
            deadline.Token);

        // The inverse control: a stream inside its lifetime has not ended.
        Assert.DoesNotContain("end", Names(before));

        clock.Advance(TimeSpan.FromSeconds(31));
        var after = await reader.ReadUntilAsync(
            read =>
            {
                return Names(read).Contains("end");
            },
            deadline.Token);

        var end = after[^1];
        Assert.Equal("end", end.Name);
        Assert.Equal("renew", end.Payload.GetProperty("reason").GetString());
    }

    /// <summary>The names of the events read so far, in order.</summary>
    /// <param name="events">The events.</param>
    /// <returns>Their names.</returns>
    private static List<string> Names(IReadOnlyList<DashboardSseEvent> events)
    {
        return [.. events.Select(read =>
        {
            return read.Name;
        })];
    }
}
