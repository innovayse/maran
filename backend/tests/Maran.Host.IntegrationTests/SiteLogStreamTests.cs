using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SitesService;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Domain;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Sites.Domain;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The site-log stream over real HTTP: <c>GET /api/v1/sites/{id}/logs</c> as server-sent events,
/// against real PostgreSQL, with only the agent replaced.
/// </summary>
/// <remarks>
/// What these tests are really about is the ending. Every one of the agent's terminal events has to
/// arrive at the browser under its own name, because an operator watching a pane cannot otherwise
/// tell a log with nothing more to say from one the agent dropped, timed out, or failed on. The
/// agent grew <c>STREAM_DROPPED</c> and <c>STREAM_IDLE</c> for exactly this, and an endpoint that
/// collapsed them into "the stream closed" would throw the whole chain away — silently, and in the
/// one direction that looks fine from the outside.
///
/// Every read is bounded by a timeout token. A tail has no natural end, so a test that waited for
/// one would hang rather than fail, and a hang is a defect that gets blamed on the runner.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class SiteLogStreamTests : IAsyncLifetime
{
    /// <summary>The password every seeded user signs in with.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The bound on every stream read, so a failure to end is a failed test and not a hang.</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SiteLogStreamTests(PostgresFixture postgres)
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

    /// <summary>A site log stream delivers its lines in order and then names how it ended.</summary>
    [Fact]
    public async Task A_site_log_stream_delivers_its_lines_in_order_and_then_names_how_it_ended()
    {
        var agent = new StubAgentSitesClient
        {
            Events =
            [
                new SiteLogEvent(SiteLogEventKind.Line, "first", true, null),
                new SiteLogEvent(SiteLogEventKind.Line, "second", false, null),
                new SiteLogEvent(SiteLogEventKind.Completed, string.Empty, false, null),
            ],
        };

        var events = await TailAsync(agent, "access", historyLines: 25);

        Assert.Equal(["line", "line", "end"], events.Select(read =>
        {
            return read.Name;
        }));
        Assert.Equal("first", events[0].Payload.GetProperty("line").GetString());
        Assert.True(events[0].Payload.GetProperty("historical").GetBoolean());
        Assert.Equal("second", events[1].Payload.GetProperty("line").GetString());
        Assert.False(events[1].Payload.GetProperty("historical").GetBoolean());
        Assert.Equal("completed", events[2].Payload.GetProperty("reason").GetString());

        // The panel addressed the agent by the OWNING account's system user, which is the only thing
        // that decides whose files are read — not by anything the caller sent.
        Assert.Equal("own", agent.RequestedAccountUsername);
        Assert.Equal("own.example.com", agent.RequestedDomain);
        Assert.Equal(SiteLogSource.Access, agent.RequestedSource);
        Assert.Equal(25u, agent.RequestedHistoryLines);
    }

    /// <summary>A dropped stream is reported as dropped and never as completed.</summary>
    [Fact]
    public async Task A_dropped_stream_is_reported_as_dropped_and_never_as_completed()
    {
        // The whole point of the chain. "Dropped" means lines were lost; "completed" would tell the
        // operator they had seen everything there was.
        var agent = Scripted(SiteLogEventKind.Dropped);

        var events = await TailAsync(agent, "error", historyLines: 0);

        Assert.Equal("end", events[^1].Name);
        Assert.Equal("dropped", events[^1].Payload.GetProperty("reason").GetString());
    }

    /// <summary>An idle stream is reported as idle and never as completed.</summary>
    [Fact]
    public async Task An_idle_stream_is_reported_as_idle_and_never_as_completed()
    {
        var agent = Scripted(SiteLogEventKind.Idle);

        var events = await TailAsync(agent, "error", historyLines: 0);

        Assert.Equal("idle", events[^1].Payload.GetProperty("reason").GetString());
    }

    /// <summary>A stream the agent ended without naming a reason is reported as truncated.</summary>
    [Fact]
    public async Task A_stream_the_agent_ended_without_naming_a_reason_is_reported_as_truncated()
    {
        // The agent client promises a terminal event. This is what the panel does when the promise
        // is broken: says so, pessimistically, rather than inventing "completed".
        var agent = new StubAgentSitesClient
        {
            Events = [new SiteLogEvent(SiteLogEventKind.Line, "orphan", false, null)],
        };

        var events = await TailAsync(agent, "access", historyLines: 0);

        Assert.Equal(["line", "end"], events.Select(read =>
        {
            return read.Name;
        }));
        Assert.Equal("truncated", events[^1].Payload.GetProperty("reason").GetString());
    }

    /// <summary>A failed stream carries a localized sentence and never the agents own text.</summary>
    [Fact]
    public async Task A_failed_stream_carries_a_localized_sentence_and_never_the_agents_own_text()
    {
        var agent = new StubAgentSitesClient
        {
            Events =
            [
                new SiteLogEvent(SiteLogEventKind.Failed, string.Empty, false, "AgentSystemFailure"),
            ],
        };

        var events = await TailAsync(agent, "error", historyLines: 0);

        Assert.Equal("failed", events[^1].Payload.GetProperty("reason").GetString());
        var message = events[^1].Payload.GetProperty("message").GetString();
        Assert.Equal(
            "This operation could not be completed on your server and nothing was changed. "
            + "Please try again in a few minutes, and contact support if it keeps happening.",
            message);
        Assert.DoesNotContain("/home/", message, StringComparison.Ordinal);
        Assert.NotEqual("AgentSystemFailure", message);
    }

    /// <summary>A caller who stops watching stops the agents tail with them.</summary>
    [Fact]
    public async Task A_caller_who_stops_watching_stops_the_agents_tail_with_them()
    {
        // Otherwise an operator closing a log pane leaves a reader running on the host for as long
        // as the process lives, and every reopened pane adds another.
        //
        // What this proves is the token plumbing: RequestAborted reaches the controller, the
        // service, and the agent client's enumeration. The last assertion reads a flag the stub
        // sets about ITSELF, which is as close as a test can get while the agent is a separate
        // root process — and the stub throws on cancellation where the shipped client yields a
        // Cancelled event instead, so the shapes differ after the token arrives. The token
        // arriving is the part under test here; what the client does with it is covered by
        // Maran.Agent.Client.Tests.
        var agent = new StubAgentSitesClient
        {
            Events = [new SiteLogEvent(SiteLogEventKind.Line, "watching", false, null)],
            WaitsForCancellation = true,
        };

        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        using var abort = new CancellationTokenSource(ReadTimeout);
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/sites/{world.OwnSiteId}/logs?source=access&historyLines=0");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, abort.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStreamAsync(abort.Token);
        using (var reader = new StreamReader(body))
        {
            // Past the opening comment frame the writer sends before any log line.
            Assert.Equal(": open", await ReadLineAsync(reader, abort.Token));
            Assert.Equal(string.Empty, await ReadLineAsync(reader, abort.Token));
            Assert.Equal("event: line", await ReadLineAsync(reader, abort.Token));
        }

        await abort.CancelAsync();

        await WaitForAsync(
            () =>
            {
                return agent.StoppedByCaller;
            },
            "the agent's tail to stop with the caller");
    }

    /// <summary>A quiet log is kept alive by a heartbeat the agent did not produce.</summary>
    [Fact]
    public async Task A_quiet_log_is_kept_alive_by_a_heartbeat_the_agent_did_not_produce()
    {
        // The frame arrives with NO agent event behind it, which is what makes the protection
        // observable at all: a proxy closes an upstream connection that has been silent for its
        // read timeout — nginx defaults to 60 s, against the agent's own 300 s idle guard — and a
        // stream torn down that way reaches the browser with no end event, the silent truncation
        // the six reasons exist to prevent.
        var agent = new StubAgentSitesClient { WaitsForCancellation = true };
        await using var factory = CreateFactory(agent, ("Sites:Logs:HeartbeatSeconds", "1"));
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        using var abort = new CancellationTokenSource(ReadTimeout);
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/sites/{world.OwnSiteId}/logs?source=access&historyLines=0");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, abort.Token);

        var body = await response.Content.ReadAsStreamAsync(abort.Token);
        using var reader = new StreamReader(body);

        // Headers reached us before any log line existed, which is the other half of the same
        // frame: until the first byte is written a pane cannot tell connecting from broken.
        Assert.Equal(": open", await ReadLineAsync(reader, abort.Token));
        Assert.Equal(string.Empty, await ReadLineAsync(reader, abort.Token));
        Assert.Equal(": keepalive", await ReadLineAsync(reader, abort.Token));

        await abort.CancelAsync();
    }

    /// <summary>A customer may not hold more log streams open at once than the limit allows.</summary>
    [Fact]
    public async Task A_customer_may_not_hold_more_log_streams_open_at_once_than_the_limit_allows()
    {
        // Each open tail pins one blocking thread in the root daemon, out of a pool shared by every
        // agent operation of every tenant — so an unbounded number of them is a cross-tenant denial
        // of service reachable through sites the caller legitimately owns. The general api policy
        // cannot express this: a fixed window's lease returns no permit when the request ends, so it
        // bounds how fast tails are opened and never how many are open.
        var agent = new StubAgentSitesClient { WaitsForCancellation = true };
        await using var factory = CreateFactory(agent, ("RateLimiting:SiteLogConcurrentStreamLimit", "2"));
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        using var abort = new CancellationTokenSource(ReadTimeout);
        var held = new List<HttpResponseMessage>();
        try
        {
            held.Add(await OpenAsync(client, world.OwnSiteId, abort.Token));
            held.Add(await OpenAsync(client, world.OwnSiteId, abort.Token));
            Assert.All(held, open =>
            {
                Assert.Equal(HttpStatusCode.OK, open.StatusCode);
            });

            using var refused = await OpenAsync(client, world.OwnSiteId, abort.Token);

            Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
            Assert.NotNull(refused.Headers.RetryAfter);
        }
        finally
        {
            await abort.CancelAsync();
            foreach (var open in held)
            {
                open.Dispose();
            }
        }
    }

    /// <summary>The stream budget belongs to the account and is not multiplied by its panel users.</summary>
    [Fact]
    public async Task The_stream_budget_belongs_to_the_account_and_is_not_multiplied_by_its_panel_users()
    {
        // The resource being rationed is consumed on behalf of a hosting account — the agent holds
        // a thread per stream and knows nothing about panel logins. Partitioned by user instead,
        // an account with five staff accounts would get five times the budget, and the limit would
        // be whatever the customer chose to make it.
        var agent = new StubAgentSitesClient { WaitsForCancellation = true };
        await using var factory = CreateFactory(agent, ("RateLimiting:SiteLogConcurrentStreamLimit", "1"));
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var customer = await SignInAsync(factory, "customer");
        using var colleague = await SignInAsync(factory, "colleague");

        using var abort = new CancellationTokenSource(ReadTimeout);
        using var held = await OpenAsync(customer, world.OwnSiteId, abort.Token);
        Assert.Equal(HttpStatusCode.OK, held.StatusCode);

        using var refused = await OpenAsync(colleague, world.OwnSiteId, abort.Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        await abort.CancelAsync();
    }

    /// <summary>Two customers on different accounts each get their own stream budget.</summary>
    [Fact]
    public async Task Two_customers_on_different_accounts_do_not_share_one_stream_budget()
    {
        // The companion to the test above, and the half that was missing.
        //
        // "One account is one budget" and "every caller shares one budget" both satisfy the
        // colleague test, because the two colleagues also share an address — so that test passes
        // unchanged if the partition key silently degrades to the caller's IP. It degrades exactly
        // that way whenever the rate limiter runs before authentication, since an unauthenticated
        // principal carries no account claim; the ordering in Program.cs is the only thing holding
        // the account partition up, and moving one line above another left the whole solution
        // green until this test existed. Two accounts on one address is the case that tells the
        // two readings apart.
        var agent = new StubAgentSitesClient { WaitsForCancellation = true };
        await using var factory = CreateFactory(agent, ("RateLimiting:SiteLogConcurrentStreamLimit", "1"));
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var customer = await SignInAsync(factory, "customer");
        using var neighbour = await SignInAsync(factory, "neighbour");

        using var abort = new CancellationTokenSource(ReadTimeout);
        using var held = await OpenAsync(customer, world.OwnSiteId, abort.Token);
        Assert.Equal(HttpStatusCode.OK, held.StatusCode);

        using var alsoHeld = await OpenAsync(neighbour, world.StrangerSiteId, abort.Token);

        Assert.Equal(HttpStatusCode.OK, alsoHeld.StatusCode);
        await abort.CancelAsync();
    }

    /// <summary>A tail a customer was allowed to open is journalled as a success naming the domain.</summary>
    [Fact]
    public async Task A_tail_a_customer_was_allowed_to_open_is_journalled_as_a_success_naming_the_domain()
    {
        // Definition of Done item 4 on the path that actually happens. A journal of refusals alone
        // answers "was anybody turned away" and never "who read this customer's log".
        var agent = Scripted(SiteLogEventKind.Completed);
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var customer = await SignInAsync(factory, "customer");

        using var deadline = new CancellationTokenSource(ReadTimeout);
        using var response = await customer.GetAsync(
            $"/api/v1/sites/{world.OwnSiteId}/logs?source=access&historyLines=0", deadline.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var admin = await SignInAsync(factory, "admin");
        using var journal = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/audit", deadline.Token));
        var tails = journal.RootElement.EnumerateArray().Where(entry =>
        {
            return entry.GetProperty("action").GetString() == "SiteLogTailed";
        }).ToList();

        var entry = Assert.Single(tails);
        Assert.True(entry.GetProperty("succeeded").GetBoolean());
        Assert.Equal("own.example.com", entry.GetProperty("subject").GetString());
    }

    /// <summary>Opens one tail and returns its response with the body still streaming.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <param name="siteId">The site whose log to open.</param>
    /// <param name="cancellationToken">The read deadline.</param>
    /// <returns>The response, headers read, body left open.</returns>
    private static async Task<HttpResponseMessage> OpenAsync(
        HttpClient client,
        Guid siteId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/sites/{siteId}/logs?source=access&historyLines=0");
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>A log request naming neither of a sites logs is refused and nothing is streamed.</summary>
    [Fact]
    public async Task A_log_request_naming_neither_of_a_sites_logs_is_refused_and_nothing_is_streamed()
    {
        var agent = Scripted(SiteLogEventKind.Completed);
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync($"/api/v1/sites/{world.OwnSiteId}/logs?source=/etc/shadow");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(agent.RequestedSource);
    }

    /// <summary>Another tenants log is not found even when the request is malformed.</summary>
    [Fact]
    public async Task Another_tenants_log_is_not_found_even_when_the_request_is_malformed()
    {
        // Order of checks, as a security property: if the parameters were validated first, a bad
        // "source" would answer 400 for a site that exists and 404 for one that does not, and the
        // pair of answers is the existence oracle the 404-never-403 rule removes.
        var agent = Scripted(SiteLogEventKind.Completed);
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync($"/api/v1/sites/{world.StrangerSiteId}/logs?source=/etc/shadow");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Reaching for another tenants log is journalled as a refusal.</summary>
    [Fact]
    public async Task Reaching_for_another_tenants_log_is_journalled_as_a_refusal()
    {
        var agent = Scripted(SiteLogEventKind.Completed);
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var customer = await SignInAsync(factory, "customer");

        await customer.GetAsync($"/api/v1/sites/{world.StrangerSiteId}/logs?source=access");

        using var admin = await SignInAsync(factory, "admin");
        using var journal = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/audit"));
        var probes = journal.RootElement.EnumerateArray().Where(entry =>
        {
            return entry.GetProperty("action").GetString() == "SiteLogTailed";
        }).ToList();

        Assert.Single(probes);
        Assert.False(probes[0].GetProperty("succeeded").GetBoolean());
        Assert.Equal(world.StrangerSiteId.ToString(), probes[0].GetProperty("subject").GetString());
    }

    /// <summary>Builds a stub whose only event is one terminal event of the given kind.</summary>
    /// <param name="kind">The ending to script.</param>
    /// <returns>The stub.</returns>
    private static StubAgentSitesClient Scripted(SiteLogEventKind kind)
    {
        return new StubAgentSitesClient { Events = [new SiteLogEvent(kind, string.Empty, false, null)] };
    }

    /// <summary>Runs one complete tail against a seeded world and returns the events it produced.</summary>
    /// <param name="agent">The scripted agent.</param>
    /// <param name="source">The <c>source</c> query value.</param>
    /// <param name="historyLines">The <c>historyLines</c> query value.</param>
    /// <returns>The server-sent events, in order, up to and including the terminal one.</returns>
    private async Task<List<SseEvent>> TailAsync(StubAgentSitesClient agent, string source, int historyLines)
    {
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        using var deadline = new CancellationTokenSource(ReadTimeout);
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/sites/{world.OwnSiteId}/logs?source={source}&historyLines={historyLines}");
        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        return await ReadEventsAsync(await response.Content.ReadAsStreamAsync(deadline.Token), deadline.Token);
    }

    /// <summary>Reads server-sent events until the terminal one, or until the deadline passes.</summary>
    /// <param name="body">The response body.</param>
    /// <param name="cancellationToken">The read deadline.</param>
    /// <returns>The events read.</returns>
    private static async Task<List<SseEvent>> ReadEventsAsync(Stream body, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(body);
        var events = new List<SseEvent>();
        var name = string.Empty;

        while (true)
        {
            var line = await ReadLineAsync(reader, cancellationToken);
            if (line is null)
            {
                return events;
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                name = line["event: ".Length..];
                continue;
            }

            if (line.StartsWith(':'))
            {
                // A comment frame: the open announcement and the keep-alives. It carries no
                // event and a browser dispatches nothing for it.
                continue;
            }

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            using var payload = JsonDocument.Parse(line["data: ".Length..]);
            events.Add(new SseEvent(name, payload.RootElement.Clone()));
            if (name == "end")
            {
                return events;
            }
        }
    }

    /// <summary>Reads one line, bounded by a timeout the reader itself does not honour.</summary>
    /// <param name="reader">The stream reader.</param>
    /// <param name="cancellationToken">The read deadline, for the reader that does observe it.</param>
    /// <returns>The line, or null at the end of the stream.</returns>
    /// <remarks>
    /// WaitAsync, and not the token alone. A read on a response stream that never produces another
    /// byte does not observe its cancellation token here, so the token by itself is a bound that
    /// does not bind — which is how one mutation run spent ten minutes inside a suite that had
    /// already stopped producing output. Every wait in this file has a real ceiling.
    /// </remarks>
    private static Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        return reader.ReadLineAsync(cancellationToken).AsTask().WaitAsync(ReadTimeout, CancellationToken.None);
    }

    /// <summary>Polls a condition until it holds, or fails the test when the bound is reached.</summary>
    /// <param name="condition">What must become true.</param>
    /// <param name="what">How to describe it when it never does.</param>
    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        // Polled with a bound rather than slept on: a sleep long enough to be reliable is a slow
        // suite, and one short enough to be fast is a flaky one (rules/testing.md "Determinism").
        // Counted in polls rather than measured against the clock, which is banned in this project.
        const int PollIntervalMilliseconds = 25;
        var polls = (int)(ReadTimeout.TotalMilliseconds / PollIntervalMilliseconds);

        for (var poll = 0; poll < polls; poll++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(PollIntervalMilliseconds));
        }

        Assert.Fail($"Timed out after {ReadTimeout.TotalSeconds:0} s waiting for {what}.");
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the scripted agent in place.</summary>
    /// <param name="agent">The agent client the host resolves.</param>
    /// <param name="settings">Extra configuration this test needs, applied last.</param>
    /// <returns>The factory.</returns>
    private WebApplicationFactory<Program> CreateFactory(
        IAgentSitesClient agent,
        params (string Key, string Value)[] settings)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);

            foreach (var setting in settings)
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            // The ONLY substitution: the agent is another process on a provisioned host and cannot
            // be present. Everything the panel itself does stays the shipped code.
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(agent);
            });
        });
    }

    /// <summary>Applies every module's migrations, the way the installer does before first boot.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SitesDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds two accounts, their users, and one site each.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The identifiers the tests address.</returns>
    private static async Task<SeededWorld> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5));
        var own = new Account(Guid.NewGuid(), "own", "own.example.com", planId, now);
        var stranger = new Account(Guid.NewGuid(), "stranger", "stranger.example.com", planId, now);
        accounts.Accounts.AddRange(own, stranger);
        await accounts.SaveChangesAsync();

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        var customer = new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now);
        customer.AssignAccount(own.Id);
        identity.Users.Add(customer);

        // A second panel user on the SAME account, so a test can ask whether the stream budget
        // belongs to the account or to the person holding the token.
        var colleague = new User(
            Guid.NewGuid(), "colleague", "colleague@example.com", hasher.Hash(Password), UserRole.Customer, now);
        colleague.AssignAccount(own.Id);
        identity.Users.Add(colleague);

        // A customer on the OTHER account, so a test can ask the opposite question: that two
        // accounts are two budgets and not one shared between everyone on this address.
        var neighbour = new User(
            Guid.NewGuid(), "neighbour", "neighbour@example.com", hasher.Hash(Password), UserRole.Customer, now);
        neighbour.AssignAccount(stranger.Id);
        identity.Users.Add(neighbour);
        await identity.SaveChangesAsync();

        var ownSite = NewSite(own.Id, "own.example.com", now);
        var strangerSite = NewSite(stranger.Id, "stranger.example.com", now);
        sites.Sites.AddRange(ownSite, strangerSite);
        await sites.SaveChangesAsync();

        return new SeededWorld(ownSite.Id, strangerSite.Id);
    }

    /// <summary>Builds one PHP-backed site row.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="now">The creation instant, from the panel's clock.</param>
    /// <returns>The site.</returns>
    private static Site NewSite(Guid accountId, string domain, DateTimeOffset now)
    {
        return new Site(
            Guid.NewGuid(),
            accountId,
            domain,
            [],
            SiteBackendType.Php,
            "8.3",
            string.Empty,
            $"/home/acct/sites/{domain}",
            now);
    }

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The user to sign in as.</param>
    /// <returns>The signed-in client.</returns>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>One server-sent event as the tests read it.</summary>
    /// <param name="Name">The event name: <c>line</c> or <c>end</c>.</param>
    /// <param name="Payload">Its JSON data.</param>
    private sealed record SseEvent(string Name, JsonElement Payload);

    /// <summary>The identifiers a seeded world hands to the tests.</summary>
    /// <param name="OwnSiteId">The site belonging to the signed-in customer.</param>
    /// <param name="StrangerSiteId">The site belonging to the other tenant.</param>
    private sealed record SeededWorld(Guid OwnSiteId, Guid StrangerSiteId);
}
