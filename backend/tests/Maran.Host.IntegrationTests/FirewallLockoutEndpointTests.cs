using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FirewallService;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Firewall.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The firewall's lock-out refusal asked at the surface an operator meets: a real HTTP request whose
/// address arrives through the forwarded-header pipeline, the real handler and the real problem
/// translation, with only the agent replaced.
/// </summary>
/// <remarks>
/// The verdict depends on a value the handler never computes: the caller's address, which the
/// controller stamps from the connection. A handler test hands that string in directly and would stay
/// green if the controller stopped stamping it, if the pipeline stopped believing the proxy, or if
/// the status mapping answered something other than 409.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class FirewallLockoutEndpointTests : IAsyncLifetime
{
    /// <summary>The password the seeded administrator signs in with.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The key the test host uses for both encryption and JWT signing.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The address the administrator appears to arrive from.</summary>
    private const string CallerAddress = "198.51.100.7";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public FirewallLockoutEndpointTests(PostgresFixture postgres)
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

    /// <summary>Narrowing the SSH port to a range that excludes the caller is refused with a 409 and a code.</summary>
    /// <returns>Resolves once the refusal and the untouched agent have been read back.</returns>
    [Fact]
    public async Task Restricting_the_ssh_port_to_a_range_without_the_caller_is_refused_and_installs_nothing()
    {
        var agent = new StubAgentFirewallClient();
        await using var factory = CreateFactory(agent);
        await MigrateAndSeedAsync(factory);
        using var client = await SignInAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/firewall/rules", new { port = 22, protocol = 1, sourceCidr = "203.0.113.0/24" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FirewallRuleLocksOutCaller", body.RootElement.GetProperty("code").GetString());
        Assert.Null(agent.RulePorts);
    }

    /// <summary>The same rule, with the caller inside the range, is installed.</summary>
    /// <returns>Resolves once the agent has been read back.</returns>
    /// <remarks>The inverse control: a guard that refused every SSH-port allow passes the test above.</remarks>
    [Fact]
    public async Task Restricting_the_ssh_port_to_a_range_with_the_caller_is_installed()
    {
        var agent = new StubAgentFirewallClient();
        await using var factory = CreateFactory(agent);
        await MigrateAndSeedAsync(factory);
        using var client = await SignInAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/firewall/rules", new { port = 22, protocol = 1, sourceCidr = "198.51.100.0/24" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal((22, (int?)null), agent.RulePorts);
    }

    /// <summary>The refused allow is journalled as a failure naming the rule.</summary>
    /// <returns>Resolves once the journal has been read back.</returns>
    [Fact]
    public async Task The_refused_allow_is_journalled_as_a_failure_naming_the_rule()
    {
        await using var factory = CreateFactory(new StubAgentFirewallClient());
        await MigrateAndSeedAsync(factory);
        using var client = await SignInAsync(factory);

        await client.PostAsJsonAsync(
            "/api/v1/firewall/rules", new { port = 22, protocol = 1, sourceCidr = "203.0.113.0/24" });

        var response = await client.GetAsync("/api/v1/audit?pageSize=100");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var refusals = body.RootElement.GetProperty("items").EnumerateArray()
            .Where(entry =>
            {
                return entry.GetProperty("action").GetString() == FirewallAuditActions.FirewallRuleAllowed
                    && !entry.GetProperty("succeeded").GetBoolean();
            })
            .Select(entry =>
            {
                return entry.GetProperty("subject").GetString();
            })
            .ToList();

        Assert.Equal(["tcp/22 from 203.0.113.0/24"], refusals);
    }

    /// <summary>Removing the rule that admits the caller, with another rule left for the port, is refused with a 409.</summary>
    /// <returns>Resolves once the refusal and the untouched agent have been read back.</returns>
    [Fact]
    public async Task Removing_the_rule_that_admits_the_caller_while_another_remains_is_refused_and_removes_nothing()
    {
        var agent = new StubAgentFirewallClient
        {
            Rules =
            [
                new AgentFirewallRule(22, null, AgentFirewallProtocol.Tcp, "198.51.100.0/24"),
                new AgentFirewallRule(22, null, AgentFirewallProtocol.Tcp, "192.0.2.0/24"),
            ],
        };
        await using var factory = CreateFactory(agent);
        await MigrateAndSeedAsync(factory);
        using var client = await SignInAsync(factory);

        var response = await client.DeleteAsync(
            "/api/v1/firewall/rules?port=22&protocol=1&sourceCidr=198.51.100.0%2F24");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FirewallRuleLocksOutCaller", body.RootElement.GetProperty("code").GetString());
        Assert.Null(agent.RulePorts);
    }

    /// <summary>The inverse control: removing the other rule, which leaves the caller admitted, goes through.</summary>
    /// <returns>Resolves once the agent has been read back.</returns>
    [Fact]
    public async Task Removing_a_rule_that_leaves_the_caller_admitted_is_sent_to_the_agent()
    {
        var agent = new StubAgentFirewallClient
        {
            Rules =
            [
                new AgentFirewallRule(22, null, AgentFirewallProtocol.Tcp, "198.51.100.0/24"),
                new AgentFirewallRule(22, null, AgentFirewallProtocol.Tcp, "192.0.2.0/24"),
            ],
        };
        await using var factory = CreateFactory(agent);
        await MigrateAndSeedAsync(factory);
        using var client = await SignInAsync(factory);

        var response = await client.DeleteAsync(
            "/api/v1/firewall/rules?port=22&protocol=1&sourceCidr=192.0.2.0%2F24");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal((22, (int?)null), agent.RulePorts);
    }

    /// <summary>Banning the address the request arrived from is refused with a 409 and reaches no agent.</summary>
    /// <returns>Resolves once the refusal and the untouched agent have been read back.</returns>
    [Fact]
    public async Task Banning_the_callers_own_address_is_refused_and_bans_nothing()
    {
        var agent = new StubAgentFirewallClient();
        await using var factory = CreateFactory(agent);
        await MigrateAndSeedAsync(factory);
        using var client = await SignInAsync(factory);

        var response = await client.PostAsJsonAsync("/api/v1/firewall/bans", new { address = CallerAddress });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("BanAddressIsCaller", body.RootElement.GetProperty("code").GetString());
        Assert.Empty(agent.Bans);
    }

    /// <summary>The inverse control: banning another address is installed.</summary>
    /// <returns>Resolves once the agent has been read back.</returns>
    [Fact]
    public async Task Banning_another_address_is_installed()
    {
        var agent = new StubAgentFirewallClient();
        await using var factory = CreateFactory(agent);
        await MigrateAndSeedAsync(factory);
        using var client = await SignInAsync(factory);

        var response = await client.PostAsJsonAsync("/api/v1/firewall/bans", new { address = "203.0.113.9" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["203.0.113.9"], agent.Bans);
    }

    /// <summary>Applies the migrations these tests need (the Identity and Firewall modules') and seeds the one administrator.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>Resolves once the tables and the row exist.</returns>
    private static async Task MigrateAndSeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await identity.Database.MigrateAsync();

        // A ban is recorded as an episode and judged against the whitelist, both of this module's tables.
        await scope.ServiceProvider.GetRequiredService<FirewallDbContext>().Database.MigrateAsync();

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));

        await identity.SaveChangesAsync();
    }

    /// <summary>Signs the administrator in from the shared address and keeps arriving from it.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>A client carrying the access token and the forwarded address.</returns>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", CallerAddress);

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "admin", Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>Boots a host that believes the forwarded header, with the agent replaced.</summary>
    /// <param name="agent">The agent double every firewall call reaches.</param>
    /// <returns>The factory; the caller disposes it.</returns>
    private WebApplicationFactory<Program> CreateFactory(StubAgentFirewallClient agent)
    {
        return new ConfiguredHostFactory(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);

            // The host's SSH port is 22: the installer's own detection, written into panel.env.
            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAgentFirewallClient>(agent);

                // Loopback, because that is where nginx is: it makes the panel honour the forwarded
                // header, which is the only way these tests can arrive from a chosen address.
                services.AddSingleton<IStartupFilter>(new RemotePeerStartupFilter(IPAddress.Loopback));
            });
        });
    }
}
