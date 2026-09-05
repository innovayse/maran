using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Firewall.Controllers;
using Maran.Modules.Firewall.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The firewall surface over real HTTP against real PostgreSQL: who may reach it, what a mutation
/// actually sends the agent, and what an administrator is told when the SERVER is the thing that is
/// wrong.
/// </summary>
/// <remarks>
/// <para>
/// The gate is a ROLE, not ownership, so the questions are not the tenant ones the site and database
/// fixtures ask. There is no tenant dimension here at all: a firewall rule, a ban and a whitelist row
/// are facts about the whole machine. An anonymous caller is answered 401 and a signed-in customer
/// 403 — the same answers <c>AccountsController</c> gives, which is the admin-gating idiom this
/// controller mirrors. 404 would be the wrong answer: it is the tenant answer, and using it here
/// would say a rule "does not exist" to a caller who is simply not an administrator.
/// </para>
/// <para>
/// The agent is the only substitution, and only because it cannot be present: it is a separate root
/// process that rewrites an nftables ruleset.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class FirewallAuthorizationTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Every route the firewall surface declares, across its three controllers.</summary>
    /// <remarks>
    /// Completeness is asserted by <see cref="Every_firewall_route_is_covered_by_the_gating_fixture"/>
    /// rather than trusted: a new route added without a row here would otherwise enjoy no proof that
    /// it is closed to a customer at all.
    /// </remarks>
    public static TheoryData<string, string> FirewallEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/firewall/rules" },
            { "POST", "/api/v1/firewall/rules" },
            { "DELETE", "/api/v1/firewall/rules" },
            { "GET", "/api/v1/firewall/bans" },
            { "POST", "/api/v1/firewall/bans" },
            { "DELETE", "/api/v1/firewall/bans" },
            { "GET", "/api/v1/firewall/whitelist" },
            { "POST", "/api/v1/firewall/whitelist" },
            { "DELETE", "/api/v1/firewall/whitelist/{id}" },
        };
    }

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public FirewallAuthorizationTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller is refused by every firewall endpoint.</summary>
    [Theory]
    [MemberData(nameof(FirewallEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_firewall_endpoint(string method, string path)
    {
        await using var factory = CreateFactory(new StubAgentFirewallClient());
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, Substitute(path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A signed in customer is refused by every firewall endpoint and told so plainly.</summary>
    [Theory]
    [MemberData(nameof(FirewallEndpoints))]
    public async Task A_signed_in_customer_is_refused_by_every_firewall_endpoint_and_told_so_plainly(
        string method,
        string path)
    {
        // 403 and deliberately not 404. The tenant rule — another customer's row answers "not
        // found" — exists so that an identifier cannot be used as an oracle. There is no tenant
        // here and no identifier to probe: the whole surface is the server's, and a customer is
        // simply not an administrator.
        await using var factory = CreateFactory(new StubAgentFirewallClient());
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, Substitute(path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An administrator opening a port sends the agent both host facts.</summary>
    [Fact]
    public async Task An_administrator_opening_a_port_sends_the_agent_both_host_facts()
    {
        // The end-to-end proof of the whole options path: panel.env -> FirewallOptions -> the agent
        // call. The agent re-renders the entire ruleset under a drop policy, so these two values are
        // what keep the operator's session and the panel reachable.
        var agent = new StubAgentFirewallClient();
        await using var factory = CreateFactory(agent, ("Firewall:SshPorts", "22,2200,2222"));
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PostAsJsonAsync(
            "/api/v1/firewall/rules", new { port = 8080, protocol = 1, sourceCidr = "0.0.0.0/0" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([22, 2200, 2222], agent.SshPorts);
        Assert.Equal(8443, agent.PanelPort);
    }

    /// <summary>Listing the rules sends the host facts too so the rulesets own accepts stay hidden.</summary>
    [Fact]
    public async Task Listing_the_rules_sends_the_host_facts_too_so_the_rulesets_own_accepts_stay_hidden()
    {
        var agent = new StubAgentFirewallClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/firewall/rules");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([22], agent.SshPorts);
        Assert.Equal(8443, agent.PanelPort);
    }

    /// <summary>A rule the caller spelled wrongly is answered as the callers mistake.</summary>
    [Fact]
    public async Task A_rule_the_caller_spelled_wrongly_is_answered_as_the_callers_mistake()
    {
        // A source range with host bits beyond its prefix. 400, because the caller can fix it.
        await using var factory = CreateFactory(new StubAgentFirewallClient());
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PostAsJsonAsync(
            "/api/v1/firewall/rules", new { port = 8080, protocol = 1, sourceCidr = "203.0.113.7/24" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>A host whose firewall settings are wrong is answered as the servers fault.</summary>
    [Fact]
    public async Task A_host_whose_firewall_settings_are_wrong_is_answered_as_the_servers_fault()
    {
        // The agent client answers AgentFirewallPortsMisconfigured for a missing Firewall__SshPorts,
        // separately from the AgentInvalidInput it uses for a bad rule port, because the two have
        // opposite audiences. 400 would tell an API caller they submitted bad details and send them
        // to check a request that was perfectly good, while the operator's panel.env goes
        // unmentioned.
        var agent = new StubAgentFirewallClient
        {
            MutationResult = SharedKernel.Results.Result<bool>.Fail(
                SharedKernel.Results.Error.Of("AgentFirewallPortsMisconfigured", SharedKernel.Results.ErrorType.Failure)),
        };
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PostAsJsonAsync(
            "/api/v1/firewall/rules", new { port = 8080, protocol = 1, sourceCidr = "0.0.0.0/0" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AgentFirewallPortsMisconfigured", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>Banning an address writes the row that will outlive the next restart.</summary>
    [Fact]
    public async Task Banning_an_address_writes_the_row_that_will_outlive_the_next_restart()
    {
        var agent = new StubAgentFirewallClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PostAsJsonAsync(
            "/api/v1/firewall/bans", new { address = "203.0.113.7", durationMinutes = 60 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["203.0.113.7"], agent.Bans);

        using var scope = factory.Services.CreateScope();
        var firewall = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();
        var episode = Assert.Single(await firewall.BanEpisodes.AsNoTracking().ToListAsync());
        Assert.Equal("203.0.113.7", episode.IpAddress);
        Assert.NotNull(episode.ExpiresAt);
    }

    /// <summary>A ban is listed back with the reason the agent could never hold.</summary>
    [Fact]
    public async Task A_ban_is_listed_back_with_the_reason_the_agent_could_never_hold()
    {
        await using var factory = CreateFactory(new StubAgentFirewallClient());
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        await client.PostAsJsonAsync("/api/v1/firewall/bans", new { address = "203.0.113.7", durationMinutes = 60 });
        var response = await client.GetAsync("/api/v1/firewall/bans");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ban = Assert.Single(body.RootElement.EnumerateArray().ToList());
        Assert.Equal("203.0.113.7", ban.GetProperty("ipAddress").GetString());
        // Camel-cased by the panel's own JSON convention (JsonSerializationExtensions), like every
        // other enum it sends.
        Assert.Equal("manual", ban.GetProperty("reason").GetString());
    }

    /// <summary>Unbanning an address the panel has no ban for answers not found.</summary>
    [Fact]
    public async Task Unbanning_an_address_the_panel_has_no_ban_for_answers_not_found()
    {
        // 404 here IS right, and for the reason 403 is right above: this one names a resource the
        // caller asked for by identifier, and it genuinely is not there.
        await using var factory = CreateFactory(new StubAgentFirewallClient());
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.DeleteAsync("/api/v1/firewall/bans?address=203.0.113.7");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A whitelist row round trips and the same range cannot be added twice.</summary>
    [Fact]
    public async Task A_whitelist_row_round_trips_and_the_same_range_cannot_be_added_twice()
    {
        await using var factory = CreateFactory(new StubAgentFirewallClient());
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var created = await client.PostAsJsonAsync(
            "/api/v1/firewall/whitelist", new { cidr = "203.0.113.7/32", note = "office" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var again = await client.PostAsJsonAsync(
            "/api/v1/firewall/whitelist", new { cidr = "203.0.113.7/32", note = "office" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var listed = await client.GetAsync("/api/v1/firewall/whitelist");
        using var body = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
        var row = Assert.Single(body.RootElement.EnumerateArray().ToList());
        Assert.Equal("203.0.113.7/32", row.GetProperty("cidr").GetString());
    }

    /// <summary>A whitelist request with no range at all is answered 400 rather than 500.</summary>
    [Fact]
    public async Task A_whitelist_request_with_no_range_at_all_is_answered_400_rather_than_500()
    {
        // The status code is the whole user-visible content of that fix, and it was asserted only
        // by running the validator directly. FluentValidation runs a .Must(...) even after the
        // .NotEmpty() before it has failed, so the missing field reached CidrRange as null and the
        // panel answered an administrator with 500 — an error that says "the server is broken"
        // about a request that is simply incomplete.
        await using var factory = CreateFactory(new StubAgentFirewallClient());
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PostAsJsonAsync("/api/v1/firewall/whitelist", new { note = "office" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>A range typed in a second spelling is stored and listed in the first one.</summary>
    [Fact]
    public async Task A_range_typed_in_a_second_spelling_is_stored_and_listed_in_the_first_one()
    {
        // 203.0.113.0/024 and 203.0.113.0/24 are one range with two spellings. Stored as typed they
        // were two rows for one exemption, so removing one left the exemption in place while the
        // screen said it had gone — and the second insert raced the column's unique index into a 500.
        await using var factory = CreateFactory(new StubAgentFirewallClient());
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var created = await client.PostAsJsonAsync(
            "/api/v1/firewall/whitelist", new { cidr = "203.0.113.0/24", note = "office" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var again = await client.PostAsJsonAsync(
            "/api/v1/firewall/whitelist", new { cidr = "203.0.113.0/024", note = "office again" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var listed = await client.GetAsync("/api/v1/firewall/whitelist");
        using var body = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
        var row = Assert.Single(body.RootElement.EnumerateArray().ToList());
        Assert.Equal("203.0.113.0/24", row.GetProperty("cidr").GetString());
    }

    /// <summary>Every mutation is journalled with the rule or address it touched.</summary>
    [Fact]
    public async Task Every_mutation_is_journalled_with_the_rule_or_address_it_touched()
    {
        await using var factory = CreateFactory(new StubAgentFirewallClient());
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        await client.PostAsJsonAsync(
            "/api/v1/firewall/rules", new { port = 8080, protocol = 1, sourceCidr = "0.0.0.0/0" });
        await client.PostAsJsonAsync("/api/v1/firewall/bans", new { address = "203.0.113.7", durationMinutes = 60 });
        await client.PostAsJsonAsync("/api/v1/firewall/whitelist", new { cidr = "198.51.100.0/24", note = "office" });

        var response = await client.GetAsync("/api/v1/audit");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var entries = body.RootElement.EnumerateArray()
            .Select(entry =>
            {
                return (entry.GetProperty("action").GetString(), entry.GetProperty("subject").GetString());
            })
            .ToList();

        Assert.Contains(("FirewallRuleAllowed", "tcp/8080 from 0.0.0.0/0"), entries);
        Assert.Contains(("AddressBanned", "203.0.113.7"), entries);
        Assert.Contains(("FirewallWhitelistChanged", "198.51.100.0/24"), entries);
    }

    /// <summary>Every firewall route is covered by the gating fixture.</summary>
    [Fact]
    public void Every_firewall_route_is_covered_by_the_gating_fixture()
    {
        // A hand-written list of routes goes stale the first time somebody adds one. These are read
        // off the controllers themselves, so a new endpoint fails HERE — naming itself — rather than
        // quietly enjoying no proof that it is closed to a customer.
        var declared = ControllerRoutes.Declared<FirewallRulesController>()
            .Concat(ControllerRoutes.Declared<FirewallBansController>())
            .Concat(ControllerRoutes.Declared<FirewallWhitelistController>())
            .ToList();
        Assert.NotEmpty(declared);

        var inFixture = FirewallEndpoints()
            .Select(row =>
            {
                return $"{row[0]} {row[1]}";
            })
            .ToHashSet(StringComparer.Ordinal);

        var missing = declared.Where(route =>
        {
            return !inFixture.Contains(route);
        }).ToList();

        Assert.True(
            missing.Count == 0,
            "These firewall routes are absent from FirewallEndpoints(), so nothing proves they are "
            + "closed to a signed-in customer: " + string.Join(", ", missing));
    }

    /// <summary>Substitutes a row identifier into a route template.</summary>
    /// <param name="path">The route template.</param>
    private static string Substitute(string path)
    {
        return path.Replace("{id}", Guid.NewGuid().ToString(), StringComparison.Ordinal);
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the agent replaced.</summary>
    /// <param name="agent">The agent double every firewall call reaches.</param>
    /// <param name="settings">Extra configuration this test needs.</param>
    private WebApplicationFactory<Program> CreateFactory(
        StubAgentFirewallClient agent,
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

            // Startup validation refuses to boot without the host's SSH ports and the panel's
            // public port: a defaulted one is a locked-out server (rules/security.md).
            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            foreach (var setting in settings)
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAgentFirewallClient>(agent);
            });
        });
    }

    /// <summary>Applies the migrations these tests need, the way the installer does before first boot.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<FirewallDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds one administrator and one customer.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task SeedUsersAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        identity.Users.Add(new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now));

        await identity.SaveChangesAsync();
    }

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The user to sign in as.</param>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>Issues one request, giving each POST route a body its model binder accepts.</summary>
    /// <remarks>
    /// The bodies must be VALID. A route whose body fails validation answers 400, which would make a
    /// gating theory pass without the request ever reaching the authorization policy under test.
    /// </remarks>
    /// <param name="client">The client to send with.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute path.</param>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), QueryFor(method, path));
        if (method == "POST")
        {
            request.Content = BodyFor(path);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Adds the query a DELETE route binds its parameters from.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute path.</param>
    private static string QueryFor(string method, string path)
    {
        if (method != "DELETE")
        {
            return path;
        }

        if (path.EndsWith("/rules", StringComparison.Ordinal))
        {
            return path + "?port=8080&protocol=1&sourceCidr=0.0.0.0%2F0";
        }

        return path.EndsWith("/bans", StringComparison.Ordinal) ? path + "?address=203.0.113.7" : path;
    }

    /// <summary>Builds a valid request body for one POST route.</summary>
    /// <param name="path">The absolute path being posted to.</param>
    private static JsonContent BodyFor(string path)
    {
        if (path.EndsWith("/rules", StringComparison.Ordinal))
        {
            return JsonContent.Create(new { port = 8080, protocol = 1, sourceCidr = "0.0.0.0/0" });
        }

        if (path.EndsWith("/bans", StringComparison.Ordinal))
        {
            return JsonContent.Create(new { address = "203.0.113.7", durationMinutes = 60 });
        }

        return JsonContent.Create(new { cidr = "203.0.113.7/32", note = "office" });
    }
}
