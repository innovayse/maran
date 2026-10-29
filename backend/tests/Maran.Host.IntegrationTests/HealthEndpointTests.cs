using System.Net;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Covers the readiness case that needs a real, reachable database — the in-memory
/// <c>Maran.Host.Tests</c> project covers the unreachable/not-configured cases without the
/// cost of a container.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class HealthEndpointTests : IAsyncLifetime
{
    private readonly TestDatabase _pg;

    /// <summary>The agent socket path nothing serves, so no test here dials the machine's own agent.</summary>
    private readonly UnreachableAgentSocket _agentSocket = UnreachableAgentSocket.Missing();

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public HealthEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _agentSocket.Dispose();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds a host against this test's own database, with the settings startup validation refuses
    /// to boot without.
    /// </summary>
    /// <remarks>
    /// Extracted so that the ready/not-ready pair below share one host definition: two copies would
    /// be two places for the settings to drift, and a readiness test built differently from the one
    /// it is the control for proves nothing about either.
    /// </remarks>
    /// <param name="agent">
    /// The handshake to answer with in place of the real client, or null to dial the pinned, unreachable
    /// socket through the real client.
    /// </param>
    /// <returns>A factory the caller disposes.</returns>
    private WebApplicationFactory<Program> NewFactory(ScriptedAgentSystemClient? agent = null)
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
            builder.UseSetting("Security:EncryptionKey", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");
            builder.UseSetting("Jwt:SigningKey", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");

            // Startup validation refuses to boot without the host's SSH ports and the panel's
            // public port: a defaulted one is a locked-out server (rules/security.md).
            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Agent:SocketPath", _agentSocket.SocketPath);
            if (agent is not null)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IAgentSystemClient>(agent);
                });
            }
        });
    }

    /// <summary>
    /// Applies every registered module's migrations to this test's database, the way step 65 does on
    /// a server.
    /// </summary>
    /// <param name="services">The built host's services.</param>
    /// <returns>Resolves when the schema exists.</returns>
    private static async Task MigrateAsync(IServiceProvider services)
    {
        foreach (var contextType in services.GetServices<DbContextOptions>()
                     .Select(options => { return options.ContextType; })
                     .Distinct())
        {
            await using var scope = services.CreateAsyncScope();
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            await context.Database.MigrateAsync();
        }
    }

    /// <summary>Readiness endpoint returns 200 when the database is reachable and migrated.</summary>
    /// <returns>Resolves when the assertion has been made.</returns>
    [Fact]
    public async Task Readiness_endpoint_returns_200_when_the_database_is_reachable()
    {
        await using var factory = NewFactory();
        using var client = factory.CreateClient();

        // The schema FIRST, because readiness now means "this panel can serve", not "a connection
        // opened". An empty database is covered by its own test below; mixing the two would leave
        // neither proved.
        await MigrateAsync(factory.Services);

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// A database that answers but holds no schema is NOT ready, and this is the inverse control for
    /// the test above: without it, both would pass for a readiness check that never looked at the
    /// database at all.
    /// </summary>
    /// <remarks>
    /// This is the state that went undetected through three releases. Nothing applied the
    /// migrations, every fresh install came up empty, and the probe reported the database as
    /// "reachable" because it had opened a connection and asked nothing further — so `/health` said
    /// the panel was fine while the first thing an operator did answered
    /// `relation "identity.Users" does not exist` (issues #66, #68).
    /// </remarks>
    /// <returns>Resolves when the assertion has been made.</returns>
    [Fact]
    public async Task Readiness_endpoint_refuses_when_the_database_has_no_schema()
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        // Deliberately NOT migrated: this is a freshly created database, exactly as the installer
        // leaves it before step 65 runs.
        var response = await client.GetAsync("/health/ready");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Readiness fails when the agent answers in a contract revision outside the window the panel
    /// drives, and says so by name — while liveness stays 200, so nothing restarts a healthy process.
    /// </summary>
    /// <remarks>
    /// Issue #88: a panel upgraded without its agent, or the reverse, used to report "connected" because
    /// the handshake only had to answer. The answer now carries the verdict, and readiness is the one
    /// route an operator's proxy reads to stop sending traffic.
    /// </remarks>
    /// <returns>Resolves when the assertions have been made.</returns>
    [Fact]
    public async Task Readiness_fails_when_the_agent_speaks_an_incompatible_contract()
    {
        await using var factory = NewFactory(new ScriptedAgentSystemClient(ScriptedAgentSystemClient.Incompatible()));
        using var client = factory.CreateClient();
        await MigrateAsync(factory.Services);

        var ready = await client.GetAsync("/health/ready");
        var live = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        using var body = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
        Assert.Equal("incompatible", body.RootElement.GetProperty("agent").GetString());
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    /// <summary>
    /// Inverse control for the refusal above: a compatible agent leaves readiness at 200, so the 503
    /// cannot be a readiness that fails for every agent.
    /// </summary>
    /// <returns>Resolves when the assertion has been made.</returns>
    [Fact]
    public async Task Readiness_returns_200_with_a_compatible_agent()
    {
        await using var factory = NewFactory(new ScriptedAgentSystemClient(ScriptedAgentSystemClient.Compatible()));
        using var client = factory.CreateClient();
        await MigrateAsync(factory.Services);

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("connected", body.RootElement.GetProperty("agent").GetString());
    }

    /// <summary>
    /// An ABSENT agent does not fail readiness: it may not be installed yet or be restarting, and the
    /// panel serves its own pages meanwhile. Only an agent that ANSWERED incompatibly does.
    /// </summary>
    /// <remarks>
    /// Goes through the real system client, the real probe pipeline and the real channel to a path
    /// nothing serves — the shape of the outage, not a stub of it.
    /// </remarks>
    /// <returns>Resolves when the assertions have been made.</returns>
    [Fact]
    public async Task Readiness_does_not_require_a_reachable_agent()
    {
        await using var factory = NewFactory();
        using var client = factory.CreateClient();
        await MigrateAsync(factory.Services);

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unavailable", body.RootElement.GetProperty("agent").GetString());
    }

    /// <summary>
    /// Readiness follows the agent: it fails while the agent is on the wrong revision and recovers when
    /// the upgrade completes, with no restart of the panel in between.
    /// </summary>
    /// <remarks>
    /// The verdict is asked afresh on every request. A readiness that cached the startup answer would
    /// keep refusing traffic after the operator finished the upgrade, which is the state an operator
    /// then has to restart the panel to leave.
    /// </remarks>
    /// <returns>Resolves when the assertions have been made.</returns>
    [Fact]
    public async Task Readiness_follows_the_agent_when_an_upgrade_completes()
    {
        var agent = new ScriptedAgentSystemClient(ScriptedAgentSystemClient.Incompatible());
        await using var factory = NewFactory(agent);
        using var client = factory.CreateClient();
        await MigrateAsync(factory.Services);

        var before = await client.GetAsync("/health/ready");
        agent.Answer(ScriptedAgentSystemClient.Compatible());
        var after = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, before.StatusCode);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    /// <summary>The human-facing summary names an incompatible agent but still answers 200.</summary>
    /// <returns>Resolves when the assertions have been made.</returns>
    [Fact]
    public async Task The_health_summary_reports_an_incompatible_agent()
    {
        await using var factory = NewFactory(new ScriptedAgentSystemClient(ScriptedAgentSystemClient.Incompatible()));
        using var client = factory.CreateClient();
        await MigrateAsync(factory.Services);

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("incompatible", body.RootElement.GetProperty("agent").GetString());
    }

    /// <summary>
    /// The host keeps running after its startup check finds an incompatible agent: the gate logs and
    /// returns, it never stops the host, so a supervisor has no crash loop to enter.
    /// </summary>
    /// <returns>Resolves when the assertions have been made.</returns>
    [Fact]
    public async Task The_host_keeps_serving_after_its_startup_check_finds_an_incompatible_agent()
    {
        await using var factory = NewFactory(new ScriptedAgentSystemClient(ScriptedAgentSystemClient.Incompatible()));
        using var client = factory.CreateClient();

        // The first request starts the host, and with it the startup check that reads the agent.
        var first = await client.GetAsync("/health/live");
        await Task.Delay(TimeSpan.FromSeconds(1));
        var second = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False(factory.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
    }
}
