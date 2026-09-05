using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Firewall.Persistence;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Whether a caller the panel COUNTS is a caller the panel can BAN, asserted across the whole path
/// rather than on either end of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The axis every existing test was blind to.</b> Three components had a unit suite apiece and
/// all three were green: <c>ClientAddress</c> rendered a peer WITH its IPv6 scope and had a test
/// saying so, the brute-force detector counted under whatever string it was handed and had tests
/// saying so, and the Firewall module's <c>IpAddressNormalizer</c> REFUSED a scoped address and had
/// two tests saying so. Every one of them described its own half correctly. Nothing anywhere
/// described the composition, so nothing noticed that it added up to a caller who could be counted
/// to the threshold, escalated, and then dropped with "nothing was banned" — measurable and
/// unanswerable. That is the shape rules/testing.md calls a check that reports on something it
/// never looked at: three green suites over a red path.
/// </para>
/// <para>
/// <b>So this test refuses to look at any of the three.</b> It names no component and asserts on no
/// intermediate spelling. It puts a scoped address on the wire the way the reverse proxy would, lets
/// the real pipeline read it, the real detector count it, the real message cross the real bus and
/// the real handler decide — and then asks the one question that spans all of it: did an address
/// reach the agent. Pointing it at <c>IpAddressNormalizer</c> instead would have made it a fourth
/// test that agrees with its own component.
/// </para>
/// <para>
/// <b>What makes it fail on the old code.</b> The ban list is empty, because the detection was
/// published carrying <c>fe80::1%3</c> and refused on receipt. The assertion on the SPELLING is the
/// second half and matters as much: a ban recorded as <c>fe80::1%3</c> would be a ban the agent
/// cannot parse — its <c>BanAddress</c> holds a Rust <c>IpAddr</c>, which rejects a scoped address
/// outright — so "an address was banned" alone would pass for an inert ban, which is the failure
/// this whole area keeps producing.
/// </para>
/// <para>
/// <b>And one test that looks somewhere the ban path cannot reach.</b> The fix has two halves — the
/// panel's edge stops rendering the scope, and the Firewall module strips it again on receipt — and
/// the second half alone is enough to make a ban land. So the two ban tests above pass even with the
/// edge reverted, which was measured rather than reasoned about. The third test reads the audit
/// journal, whose recorded address nothing downstream normalises, and that is what actually holds
/// the edge in place; the same expression is the partition key for the login and password-reset rate
/// limiters, which have no comparable place to be observed from the outside.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ScopedAddressBanCompositionTests : IAsyncLifetime
{
    /// <summary>
    /// The scoped address the caller arrives as. A link-local address wearing an interface index —
    /// the one spelling the panel could count and could not ban.
    /// </summary>
    private const string ScopedAddress = "fe80::1%3";

    /// <summary>The same caller as the agent's ban set is able to name them.</summary>
    private const string BannableAddress = "fe80::1";

    /// <summary>The key the test host uses for both encryption and JWT signing.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The name the refused sign-in attempts are made under; no such user exists.</summary>
    private const string Username = "intruder";

    /// <summary>
    /// How many refused sign-ins this test makes an attack. Lowered from the shipped twenty-five
    /// through the option that exists for it, because the number is not what is under test and the
    /// default sits above the login rate limiter's per-address budget of five — which would answer
    /// the later attempts with 429 and never let them reach the detector at all.
    /// </summary>
    private const int Threshold = 3;

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ScopedAddressBanCompositionTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    /// <returns>Resolves once this test's database exists.</returns>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    /// <returns>Resolves immediately; the shared server outlives the test.</returns>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>A caller counted under a scoped address is banned under one the agent can install.</summary>
    /// <returns>Resolves once the ban has been observed, or the wait for it has run out.</returns>
    [Fact]
    public async Task A_caller_counted_under_a_scoped_address_is_banned_under_one_the_agent_can_install()
    {
        var agent = new StubAgentFirewallClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ScopedAddress);

        for (var attempt = 0; attempt < Threshold; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { Username, Password = "not the password" });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // The detection is PUBLISHED rather than invoked, so the sign-in has already answered by the
        // time the ban is installed. Waiting for the effect is the test matching the design, not a
        // sleep hiding a race: a bounded wait that ends the moment the ban lands.
        await WaitForBanAsync(agent);

        var banned = Assert.Single(agent.Bans);
        Assert.Equal(BannableAddress, banned);
        Assert.DoesNotContain("%", banned, StringComparison.Ordinal);
    }

    /// <summary>The episode the panel stored names the caller the same way the agent was told.</summary>
    /// <returns>Resolves once the stored episode has been read back.</returns>
    /// <remarks>
    /// The panel's own record has to agree with what it asked the agent to do, or the escalation
    /// ladder counts a repeat offender under a name their next ban will never be filed under and
    /// every wave reads as a first offence — a permanent fifteen minutes however long the attack
    /// runs.
    /// </remarks>
    [Fact]
    public async Task The_episode_the_panel_stored_names_the_caller_the_same_way_the_agent_was_told()
    {
        var agent = new StubAgentFirewallClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ScopedAddress);

        for (var attempt = 0; attempt < Threshold; attempt++)
        {
            await client.PostAsJsonAsync("/api/v1/auth/login", new { Username, Password = "wrong" });
        }

        var episodes = await WaitForEpisodesAsync(factory);

        Assert.Equal(BannableAddress, Assert.Single(episodes));
    }

    /// <summary>The journal names the caller in the one spelling every part of the panel uses.</summary>
    /// <returns>Resolves once the journalled attempts have been read back.</returns>
    /// <remarks>
    /// <para>
    /// <b>This is the half the other two tests cannot see.</b> They observe what the FIREWALL module
    /// installed, and that module normalises again on receipt — so reverting the panel's edge alone,
    /// leaving <c>ClientAddress</c> to render <c>fe80::1%3</c>, keeps both of them green while every
    /// consumer that is not the ban path silently goes back to the scoped spelling. That was
    /// measured by half-reverting the edge and watching them pass.
    /// </para>
    /// <para>
    /// The audit journal is the consumer that proves it, because nothing downstream re-normalises a
    /// recorded field: the address the panel writes here is the address a person reads afterwards.
    /// It is also the one cost the ban argument does not cover — two link-local machines journalled
    /// under one name cannot be told apart after the fact — so pinning the spelling here pins the
    /// rate-limit partition keys built from the same expression at the same time.
    /// </para>
    /// <para>
    /// No wait: the journal write is awaited inside the request that made it, so the rows exist by
    /// the time the last sign-in has answered.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_journal_names_the_caller_in_the_spelling_the_whole_panel_shares()
    {
        var agent = new StubAgentFirewallClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ScopedAddress);

        for (var attempt = 0; attempt < Threshold; attempt++)
        {
            await client.PostAsJsonAsync("/api/v1/auth/login", new { Username, Password = "wrong" });
        }

        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var journalled = await identity.AuditEvents
            .Where(entry => entry.Action == AuditActions.LoginFailed)
            .Select(entry => entry.IpAddress)
            .ToListAsync();

        Assert.Equal(Threshold, journalled.Count);
        Assert.All(journalled, address =>
        {
            Assert.Equal(BannableAddress, address);
        });
    }

    /// <summary>Waits until the panel has asked the agent to ban somebody, or gives up.</summary>
    /// <param name="agent">The stub agent, which records what it was asked.</param>
    /// <returns>Resolves when a ban has been recorded, or when the wait has run out.</returns>
    /// <remarks>
    /// Bounded rather than indefinite so that the failure this test exists to catch is reported as
    /// a failed assertion on an empty ban list — which says what went wrong — instead of as a run
    /// that hangs until the suite is killed.
    /// </remarks>
    private static async Task WaitForBanAsync(StubAgentFirewallClient agent)
    {
        // Stopwatch rather than the ambient clock, which is a banned API in this repository even in
        // a test (rules/csharp.md). Polling rather than sleeping (rules/testing.md "Determinism").
        var waited = Stopwatch.StartNew();

        while (agent.Bans.Count == 0 && waited.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    /// <summary>Waits until the panel has STORED a ban episode, or gives up.</summary>
    /// <param name="factory">The test host, whose services reach this test's database.</param>
    /// <returns>The addresses of the episodes that were stored; empty when the wait ran out.</returns>
    /// <remarks>
    /// <para>
    /// <b>The wait is on the row, because the row is what the test asserts.</b> Waiting on
    /// <c>agent.Bans</c> instead was a real race that failed three runs in five: the handler calls
    /// <c>BanAsync</c> and only afterwards adds the <c>BanEpisode</c> and saves it, so a test woken
    /// by the ban reads the table inside that window. It failed with "the collection was empty" —
    /// the same string the defect this class exists for produces — which would have read as the
    /// fixed bug regressing.
    /// </para>
    /// <para>
    /// Longer waiting would not have fixed it, only lengthened the fuse (rules/testing.md forbids
    /// both the sleep and the fixed-delay retry): the two writes are not ordered by elapsed time,
    /// they are ordered by the handler, so the only sound signal is the second one. A poll on the
    /// asserted effect ends the moment that effect exists and is bounded only so that a genuine
    /// failure is reported as an assertion rather than as a hung suite.
    /// </para>
    /// <para>
    /// A fresh scope per attempt, because a context that has already answered this query would
    /// answer it from its change tracker on the next one and never see the row arrive.
    /// </para>
    /// </remarks>
    private static async Task<List<string>> WaitForEpisodesAsync(WebApplicationFactory<Program> factory)
    {
        // Stopwatch rather than the ambient clock, which is a banned API in this repository even in
        // a test (rules/csharp.md). Polling rather than sleeping (rules/testing.md "Determinism").
        var waited = Stopwatch.StartNew();

        while (true)
        {
            using var scope = factory.Services.CreateScope();
            var firewall = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();
            var episodes = await firewall.BanEpisodes
                .Select(episode => episode.IpAddress)
                .ToListAsync();

            if (episodes.Count > 0 || waited.Elapsed >= TimeSpan.FromSeconds(30))
            {
                return episodes;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    /// <summary>Applies the schemas this test's fresh database does not have yet.</summary>
    /// <param name="factory">The test host, whose services hold the modules' contexts.</param>
    /// <returns>Resolves once both modules' tables exist.</returns>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<FirewallDbContext>().Database.MigrateAsync();
    }

    /// <summary>Builds a test host that believes the forwarded header and cannot reach a real agent.</summary>
    /// <param name="agent">The stub standing in for the root process that owns the ban set.</param>
    /// <returns>The factory; the caller disposes it.</returns>
    private WebApplicationFactory<Program> CreateFactory(StubAgentFirewallClient agent)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Testing, not Development: inheriting the developer's database settings made these
            // tests pass locally against the wrong database and fail in CI.
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);
            builder.UseSetting(
                "BruteForce:MaxFailuresPerAddress",
                Threshold.ToString(CultureInfo.InvariantCulture));

            // Startup validation refuses to boot without the host's SSH ports and the panel's
            // public port: a defaulted one is a locked-out server (rules/security.md).
            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAgentFirewallClient>(agent);

                // Loopback, because that is where nginx is: it makes the panel honour the forwarded
                // header, which is the only way a scoped address gets in at all. The header is the
                // input under test, so it has to be believed for the test to be about anything.
                services.AddSingleton<IStartupFilter>(new RemotePeerStartupFilter(IPAddress.Loopback));
            });
        });
    }
}
