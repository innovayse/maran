using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The pending updates surface over real HTTP against real PostgreSQL (issues #324 and #163): what an
/// administrator reads, how each update is classified and filtered, whether the host needs a restart,
/// what a failed read answers, and that a customer reaches none of it.
/// </summary>
/// <remarks>
/// Who may reach the routes is also asserted, route by route and in both directions, by
/// <see cref="MonitoringAuthorizationTests"/>, where both are listed in the gating fixture. This
/// class covers the shapes on the wire. The agent is the only substitution, and only because it is a
/// separate root process that runs the host's package manager.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class PendingUpdatesEndpointTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The password both seeded users are given.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public PendingUpdatesEndpointTests(PostgresFixture postgres)
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

    /// <summary>An administrator reads the page with every field, a missing installed version as null.</summary>
    [Fact]
    public async Task An_administrator_reads_the_page_with_every_field()
    {
        await using var factory = CreateFactory(Reporting(
            new AgentPendingUpdate(
                "openssl", "3.0.13-2", "3.0.13-1", "noble-security", AgentUpdateClassification.Security),
            new AgentPendingUpdate("bash", "5.2-2", null, string.Empty, AgentUpdateClassification.Ordinary)));
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/updates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("total").GetInt32());

        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal("bash", items[0].GetProperty("packageName").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("installedVersion").ValueKind);
        Assert.Equal(string.Empty, items[0].GetProperty("source").GetString());
        Assert.Equal("openssl", items[1].GetProperty("packageName").GetString());
        Assert.Equal("3.0.13-2", items[1].GetProperty("availableVersion").GetString());
        Assert.Equal("3.0.13-1", items[1].GetProperty("installedVersion").GetString());
        Assert.Equal("noble-security", items[1].GetProperty("source").GetString());
        Assert.Equal("security", items[1].GetProperty("classification").GetString());
        Assert.Equal("ordinary", items[0].GetProperty("classification").GetString());
        Assert.Equal("Security", items[1].GetProperty("classificationName").GetString());
        Assert.Equal("Ordinary", items[0].GetProperty("classificationName").GetString());
    }

    /// <summary>An administrator reads the classification choices in rank order, each with its name.</summary>
    [Fact]
    public async Task An_administrator_reads_the_classification_choices_in_order_with_their_names()
    {
        await using var factory = CreateFactory(Reporting());
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/updates/classifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var choices = body.RootElement.EnumerateArray().ToList();
        Assert.Equal(
            ["security", "ordinary", "unmeasured"],
            choices.Select(choice =>
            {
                return choice.GetProperty("value").GetString()!;
            }));
        Assert.Equal(
            ["Security", "Ordinary", "Not measured"],
            choices.Select(choice =>
            {
                return choice.GetProperty("name").GetString()!;
            }));
    }

    /// <summary>An anonymous caller is refused the classification choices.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused_the_classification_choices()
    {
        await using var factory = CreateFactory(Reporting());
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/monitoring/updates/classifications");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A search, a sort and a page are applied to the agent's whole answer.</summary>
    [Fact]
    public async Task A_search_a_sort_and_a_page_are_applied_to_the_whole_answer()
    {
        await using var factory = CreateFactory(Reporting(
            Ordinary("libssl3"),
            Ordinary("openssl"),
            Ordinary("bash")));
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/updates?search=SSL&sort=-packageName&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(
            ["openssl", "libssl3"],
            body.RootElement.GetProperty("items").EnumerateArray()
                .Select(row =>
                {
                    return row.GetProperty("packageName").GetString()!;
                })
                .ToArray());
    }

    /// <summary>A catalogue read successfully with nothing pending is an empty page, not an error.</summary>
    [Fact]
    public async Task A_catalogue_read_successfully_with_nothing_pending_is_an_empty_page()
    {
        await using var factory = CreateFactory(Reporting());
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/updates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>The count is the number of updates the agent reported.</summary>
    [Fact]
    public async Task The_count_is_the_number_of_updates_the_agent_reported()
    {
        await using var factory = CreateFactory(Reporting(
            Ordinary("bash"),
            Security("curl")));
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/updates/count");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("securityCount").GetInt32());
    }

    /// <summary>The security count is null, not zero, while any update's classification was not measured.</summary>
    [Fact]
    public async Task The_security_count_is_null_while_any_update_is_unmeasured()
    {
        await using var factory = CreateFactory(Reporting(
            Security("openssl"),
            new AgentPendingUpdate("bash", "2", "1", "updates", AgentUpdateClassification.Unmeasured)));
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/updates/count");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("securityCount").ValueKind);
    }

    /// <summary>The security count is zero when every update was measured and none is a security one.</summary>
    [Fact]
    public async Task The_security_count_is_zero_when_every_update_was_measured_and_none_is_security()
    {
        await using var factory = CreateFactory(Reporting(Ordinary("bash")));
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/updates/count");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("securityCount").GetInt32());
    }

    /// <summary>The classification filter keeps exactly the rows of the named classification, server side.</summary>
    /// <param name="classification">The filter value.</param>
    /// <param name="expected">The package names that must remain, in package name order.</param>
    [Theory]
    [InlineData("security", new[] { "openssl" })]
    [InlineData("ordinary", new[] { "bash", "curl" })]
    [InlineData("unmeasured", new[] { "zlib" })]
    public async Task The_classification_filter_keeps_only_the_rows_of_that_classification(
        string classification,
        string[] expected)
    {
        await using var factory = CreateFactory(Reporting(
            Ordinary("curl"),
            Security("openssl"),
            Ordinary("bash"),
            new AgentPendingUpdate("zlib", "2", "1", "updates", AgentUpdateClassification.Unmeasured)));
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync($"/api/v1/monitoring/updates?classification={classification}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected.Length, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(expected, PackageNames(body));
    }

    /// <summary>Sorting by security lists security updates first and descending lists them last.</summary>
    [Fact]
    public async Task Sorting_by_security_lists_security_updates_first_and_descending_last()
    {
        await using var factory = CreateFactory(Reporting(
            Ordinary("bash"),
            Security("openssl"),
            Ordinary("curl"),
            Security("libc6")));
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var ascending = await client.GetAsync("/api/v1/monitoring/updates?sort=security");
        var descending = await client.GetAsync("/api/v1/monitoring/updates?sort=-security");

        using var ascendingBody = JsonDocument.Parse(await ascending.Content.ReadAsStringAsync());
        using var descendingBody = JsonDocument.Parse(await descending.Content.ReadAsStringAsync());
        Assert.Equal(["libc6", "openssl", "bash", "curl"], PackageNames(ascendingBody));
        // The tie-breaker flips with the direction, so each run of equals is reversed too.
        Assert.Equal(["curl", "bash", "openssl", "libc6"], PackageNames(descendingBody));
    }

    /// <summary>A classification the list does not offer is refused with its code.</summary>
    [Fact]
    public async Task An_unknown_classification_is_refused_with_its_code()
    {
        await using var factory = CreateFactory(Reporting(Ordinary("bash")));
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/updates?classification=critical");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PendingUpdateClassificationUnknown", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>An administrator reads whether a restart is required: true, false, or null when unmeasured.</summary>
    /// <param name="requirement">What the agent measured.</param>
    /// <param name="expected">The <c>required</c> value on the wire.</param>
    [Theory]
    [InlineData(AgentRebootRequirement.Required, JsonValueKind.True)]
    [InlineData(AgentRebootRequirement.NotRequired, JsonValueKind.False)]
    [InlineData(AgentRebootRequirement.Unmeasured, JsonValueKind.Null)]
    public async Task An_administrator_reads_whether_a_restart_is_required(
        AgentRebootRequirement requirement,
        JsonValueKind expected)
    {
        var agent = new StubAgentMonitorClient
        {
            RebootRequirement = Result<AgentRebootRequirement>.Ok(requirement),
        };
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/updates/reboot-required");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, body.RootElement.GetProperty("required").ValueKind);
    }

    /// <summary>A probe the agent could not run fails the read with its typed status, never "not required".</summary>
    /// <param name="type">The kind the agent's failure carries.</param>
    /// <param name="code">The code the failure carries.</param>
    /// <param name="status">The status that kind must answer with.</param>
    [Theory]
    [InlineData(ErrorType.Failure, "AgentSystemFailure", HttpStatusCode.InternalServerError)]
    [InlineData(ErrorType.Unavailable, "AgentUnavailable", HttpStatusCode.ServiceUnavailable)]
    public async Task A_reboot_probe_the_agent_could_not_run_fails_the_read_and_is_never_not_required(
        ErrorType type,
        string code,
        HttpStatusCode status)
    {
        var agent = new StubAgentMonitorClient
        {
            RebootRequirement = Result<AgentRebootRequirement>.Fail(Error.Of(code, type)),
        };
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/updates/reboot-required");

        Assert.Equal(status, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        Assert.False(body.RootElement.TryGetProperty("required", out _));
    }

    /// <summary>An anonymous caller is refused the reboot requirement.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused_the_reboot_requirement()
    {
        var agent = new StubAgentMonitorClient
        {
            RebootRequirement = Result<AgentRebootRequirement>.Ok(AgentRebootRequirement.Required),
        };
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/monitoring/updates/reboot-required");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("required", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>A catalogue the agent could not read fails both routes and is never an empty page or a zero.</summary>
    /// <param name="path">The route read.</param>
    [Theory]
    [InlineData("/api/v1/monitoring/updates")]
    [InlineData("/api/v1/monitoring/updates/count")]
    public async Task A_catalogue_the_agent_could_not_read_fails_the_read_and_is_never_empty_or_zero(string path)
    {
        var agent = new StubAgentMonitorClient
        {
            PendingUpdates = Result<IReadOnlyList<AgentPendingUpdate>>.Fail(
                Error.Of("AgentSystemFailure", ErrorType.Failure)),
        };
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AgentSystemFailure", body.RootElement.GetProperty("code").GetString());
        Assert.False(body.RootElement.TryGetProperty("count", out _));
        Assert.False(body.RootElement.TryGetProperty("items", out _));
    }

    /// <summary>A request outside the paging contract is refused with its code.</summary>
    /// <param name="queryString">The offending query string.</param>
    /// <param name="code">The code the refusal must carry.</param>
    [Theory]
    [InlineData("sort=availableVersion", "ListSortUnknown")]
    [InlineData("pageSize=7", "ListPageSizeNotOffered")]
    [InlineData("page=0", "ListPageOutOfRange")]
    public async Task A_request_outside_the_paging_contract_is_refused_with_its_code(string queryString, string code)
    {
        await using var factory = CreateFactory(Reporting(Ordinary("bash")));
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync($"/api/v1/monitoring/updates?{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>A signed-in customer is refused every route and told nothing the agent reported.</summary>
    /// <param name="path">The route read.</param>
    [Theory]
    [InlineData("/api/v1/monitoring/updates")]
    [InlineData("/api/v1/monitoring/updates/count")]
    [InlineData("/api/v1/monitoring/updates/reboot-required")]
    [InlineData("/api/v1/monitoring/updates/classifications")]
    public async Task A_signed_in_customer_is_refused_and_told_nothing_the_agent_reported(string path)
    {
        var agent = Reporting(Ordinary("bash"));
        agent.RebootRequirement = Result<AgentRebootRequirement>.Ok(AgentRebootRequirement.Required);
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("bash", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>An ordinary (not security) update of the named package.</summary>
    /// <param name="name">The package name.</param>
    /// <returns>The update as the agent reports it.</returns>
    private static AgentPendingUpdate Ordinary(string name)
    {
        return new AgentPendingUpdate(name, "2", "1", "updates", AgentUpdateClassification.Ordinary);
    }

    /// <summary>A security update of the named package.</summary>
    /// <param name="name">The package name.</param>
    /// <returns>The update as the agent reports it.</returns>
    private static AgentPendingUpdate Security(string name)
    {
        return new AgentPendingUpdate(name, "2", "1", "updates-security", AgentUpdateClassification.Security);
    }

    /// <summary>The package names of a page, in the order the page lists them.</summary>
    /// <param name="body">The parsed page.</param>
    /// <returns>The package names.</returns>
    private static string[] PackageNames(JsonDocument body)
    {
        return body.RootElement.GetProperty("items").EnumerateArray()
            .Select(row =>
            {
                return row.GetProperty("packageName").GetString()!;
            })
            .ToArray();
    }

    /// <summary>An agent double reporting exactly the given updates.</summary>
    /// <param name="updates">What the agent reports as pending.</param>
    /// <returns>The double.</returns>
    private static StubAgentMonitorClient Reporting(params AgentPendingUpdate[] updates)
    {
        return new StubAgentMonitorClient
        {
            PendingUpdates = Result<IReadOnlyList<AgentPendingUpdate>>.Ok(updates),
        };
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the agent replaced.</summary>
    /// <param name="agent">The agent double this run answers from.</param>
    /// <returns>The booted host factory.</returns>
    private WebApplicationFactory<Program> CreateFactory(StubAgentMonitorClient agent)
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

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAgentMonitorClient>(agent);
            });
        });
    }

    /// <summary>Applies the migrations these tests need, the way the installer does before first boot.</summary>
    /// <remarks>Identity only, for the sign-in: the routes under test store nothing.</remarks>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
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
    /// <returns>A client whose requests carry the user's bearer token.</returns>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }
}
