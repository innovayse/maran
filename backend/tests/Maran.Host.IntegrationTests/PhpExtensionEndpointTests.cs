using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Tasks.Persistence;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// <c>GET</c> and <c>PUT /api/v1/sites/php-versions/{version}/extensions</c> over real HTTP and real
/// PostgreSQL, with only the agent replaced (issue #157).
/// </summary>
/// <remarks>
/// What only the real endpoint can observe: that a customer's token is refused the toggle before the
/// agent is reached, that the customer's list leaves the availability out of the JSON, that the
/// administrator's toggle is journalled under its action, and that the version list carries the
/// support life and the administrator-only site count.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class PhpExtensionEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public PhpExtensionEndpointTests(PostgresFixture postgres)
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

    /// <summary>An administrator's toggle answers the extensions as they are afterwards and is journalled.</summary>
    [Fact]
    public async Task An_administrator_switching_an_extension_on_gets_the_new_state_and_a_journal_entry()
    {
        var agent = Agent(
            [
                new PhpExtensionEvent(PhpExtensionEventKind.Progress, 40, "install", string.Empty, string.Empty, false, null),
                new PhpExtensionEvent(PhpExtensionEventKind.Changed, 100, "verify", "8.3", "gd", true, null),
            ]);
        await using var factory = await BootAsync(agent);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var response = await admin.PutAsJsonAsync(
            "/api/v1/sites/php-versions/8.3/extensions/gd",
            new { enabled = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var gd = body.RootElement.EnumerateArray().Single(entry => { return entry.GetProperty("name").GetString() == "gd"; });
        Assert.True(gd.GetProperty("enabled").GetBoolean());
        Assert.Equal("available", gd.GetProperty("availability").GetString());
        Assert.Equal(("8.3", "gd", true), agent.LastChange);

        using var journal = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/audit?action=PhpExtensionChanged"));
        var entry = journal.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.True(entry.GetProperty("succeeded").GetBoolean());
        Assert.Equal("8.3: gd enabled", entry.GetProperty("subject").GetString());
    }

    /// <summary>A body that names a version or an extension is ignored: the route decides what is switched.</summary>
    [Fact]
    public async Task A_body_naming_another_extension_cannot_override_the_route()
    {
        var agent = Agent([]);
        await using var factory = await BootAsync(agent);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var response = await admin.PutAsJsonAsync(
            "/api/v1/sites/php-versions/8.3/extensions/gd",
            new { enabled = true, extension = "redis", version = "7.4" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("8.3", "gd", true), agent.LastChange);
    }

    /// <summary>A customer's token cannot toggle an extension, and the agent is never asked.</summary>
    [Fact]
    public async Task A_customer_cannot_switch_an_extension_and_the_agent_is_never_asked()
    {
        var agent = Agent([]);
        await using var factory = await BootAsync(agent);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        await EndpointHarness.SeedCustomerAsync(factory, "customer", accountId);
        using var customer = await EndpointHarness.SignInAsync(factory, "customer");

        using var response = await customer.PutAsJsonAsync(
            "/api/v1/sites/php-versions/8.3/extensions/gd",
            new { enabled = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, agent.ChangeCalls);
    }

    /// <summary>An anonymous caller cannot toggle an extension.</summary>
    [Fact]
    public async Task An_anonymous_caller_cannot_switch_an_extension()
    {
        var agent = Agent([]);
        await using var factory = await BootAsync(agent);
        using var anonymous = factory.CreateClient();

        using var response = await anonymous.PutAsJsonAsync(
            "/api/v1/sites/php-versions/8.3/extensions/gd",
            new { enabled = true });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, agent.ChangeCalls);
    }

    /// <summary>A name outside the catalogue and a malformed version are refused before the agent is asked.</summary>
    /// <param name="path">The route to PUT to.</param>
    /// <param name="code">The code the refusal must carry.</param>
    [Theory]
    [InlineData("/api/v1/sites/php-versions/8.3/extensions/xdebug", "PhpExtensionNotAllowed")]
    [InlineData("/api/v1/sites/php-versions/8.3/extensions/GD", "PhpExtensionNotAllowed")]
    [InlineData("/api/v1/sites/php-versions/latest/extensions/gd", "PhpVersionInvalidFormat")]
    public async Task An_unknown_extension_or_a_malformed_version_is_refused_before_the_agent_is_asked(
        string path,
        string code)
    {
        var agent = Agent([]);
        await using var factory = await BootAsync(agent);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var response = await admin.PutAsJsonAsync(path, new { enabled = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, agent.ChangeCalls);
    }

    /// <summary>A failed change answers the status of its kind and is journalled as a failure.</summary>
    [Fact]
    public async Task A_failed_change_answers_the_status_of_its_kind_and_is_journalled_as_a_failure()
    {
        var agent = Agent(
            [
                new PhpExtensionEvent(
                    PhpExtensionEventKind.Failed,
                    0,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    false,
                    Error.Of("AgentPackageSourceRefused", ErrorType.UpstreamRefused)),
            ]);
        await using var factory = await BootAsync(agent);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var response = await admin.PutAsJsonAsync(
            "/api/v1/sites/php-versions/8.3/extensions/gd",
            new { enabled = true });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AgentPackageSourceRefused", problem.RootElement.GetProperty("code").GetString());

        using var journal = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/audit?action=PhpExtensionChanged"));
        Assert.False(journal.RootElement.GetProperty("items").EnumerateArray().Single().GetProperty("succeeded").GetBoolean());
    }

    /// <summary>An administrator's list carries the availability of each extension.</summary>
    [Fact]
    public async Task An_administrators_list_carries_the_availability_of_each_extension()
    {
        await using var factory = await BootAsync(Agent([]));
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var body = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/sites/php-versions/8.3/extensions"));

        var redis = body.RootElement.EnumerateArray().Single(entry => { return entry.GetProperty("name").GetString() == "redis"; });
        Assert.Equal("notPackaged", redis.GetProperty("availability").GetString());
        Assert.Equal("Not offered on this server", redis.GetProperty("availabilityName").GetString());
        Assert.Equal("Redis client (Redis)", redis.GetProperty("label").GetString());
    }

    /// <summary>A customer's list carries name, label and state, and no availability in any form.</summary>
    [Fact]
    public async Task A_customers_list_carries_no_availability()
    {
        await using var factory = await BootAsync(Agent([]));
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        await EndpointHarness.SeedCustomerAsync(factory, "customer", accountId);
        using var customer = await EndpointHarness.SignInAsync(factory, "customer");

        var json = await customer.GetStringAsync("/api/v1/sites/php-versions/8.3/extensions");

        using var body = JsonDocument.Parse(json);
        Assert.NotEmpty(body.RootElement.EnumerateArray());
        Assert.All(body.RootElement.EnumerateArray(), entry =>
        {
            Assert.True(entry.TryGetProperty("name", out _));
            Assert.True(entry.TryGetProperty("label", out _));
            Assert.True(entry.TryGetProperty("enabled", out _));
            foreach (var member in new List<string> { "availability", "availabilityName" })
            {
                if (entry.TryGetProperty(member, out var absent))
                {
                    Assert.Equal(JsonValueKind.Null, absent.ValueKind);
                }
            }
        });
        // What the administrator reads for a package-less extension must not reach a customer at all.
        Assert.DoesNotContain("notPackaged", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Not offered on this server", json, StringComparison.Ordinal);
    }

    /// <summary>The version list carries the support life for everyone and the site count for the administrator only.</summary>
    [Fact]
    public async Task The_version_list_carries_the_support_life_and_the_site_count_for_the_administrator_only()
    {
        await using var factory = await BootAsync(Agent([]));
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        await EndpointHarness.SeedCustomerAsync(factory, "customer", accountId);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var customer = await EndpointHarness.SignInAsync(factory, "customer");

        using var forAdmin = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/sites/php-versions"));
        using var forCustomer = JsonDocument.Parse(await customer.GetStringAsync("/api/v1/sites/php-versions"));

        var adminEntry = forAdmin.RootElement.EnumerateArray().Single();
        var lifecycle = adminEntry.GetProperty("lifecycle");
        Assert.True(lifecycle.TryGetProperty("status", out _));
        Assert.True(lifecycle.TryGetProperty("statusName", out _));
        Assert.True(lifecycle.TryGetProperty("endOfLife", out _));
        Assert.True(lifecycle.TryGetProperty("daysLeft", out _));
        Assert.Equal(0, adminEntry.GetProperty("sitesUsing").GetInt32());

        var customerEntry = forCustomer.RootElement.EnumerateArray().Single();
        Assert.True(customerEntry.TryGetProperty("lifecycle", out _));
        if (customerEntry.TryGetProperty("sitesUsing", out var sitesUsing))
        {
            Assert.Equal(JsonValueKind.Null, sitesUsing.ValueKind);
        }
    }

    /// <summary>Builds the scripted agent for version 8.3 with a staged change stream.</summary>
    /// <param name="events">The change stream to replay; empty answers a plain success.</param>
    /// <returns>The scripted client.</returns>
    private static ScriptedExtensionsAgentPhpClient Agent(IReadOnlyList<PhpExtensionEvent> events)
    {
        IReadOnlyList<PhpExtensionEvent> stream = events.Count > 0
            ? events
            : [new PhpExtensionEvent(PhpExtensionEventKind.Changed, 100, "verify", "8.3", string.Empty, true, null)];

        return new ScriptedExtensionsAgentPhpClient(
            [
                new PhpExtensionDto("gd", false, PhpExtensionAvailability.Available),
                new PhpExtensionDto("opcache", true, PhpExtensionAvailability.Available),
                new PhpExtensionDto("redis", false, PhpExtensionAvailability.NotPackaged),
            ],
            stream);
    }

    /// <summary>Boots the host with the scripted agent, migrates what the endpoints touch, and seeds the administrator.</summary>
    /// <param name="agent">The scripted PHP client.</param>
    /// <returns>The booted host, which the caller disposes.</returns>
    private async Task<WebApplicationFactory<Program>> BootAsync(IAgentPhpClient agent)
    {
        var factory = EndpointHarness.CreateFactory(_pg, services =>
        {
            services.AddSingleton(agent);
        });
        await EndpointHarness.MigrateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<TasksDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<SitesDbContext>().Database.MigrateAsync();
        }

        await EndpointHarness.SeedAdministratorAsync(factory);

        return factory;
    }
}
