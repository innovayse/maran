using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
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
/// The host disk view end to end: a measurement from the agent joined to a plan's allowance read out
/// of the real <c>accounts</c> schema, over real HTTP.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not only a unit test.</b> The quota travels from the <c>Plans</c> table, through
/// the widened <c>AccountSnapshot</c>, across the Sdk boundary into a module that may not reference
/// Accounts at all, and out as JSON. A unit test hands the handler a snapshot somebody already
/// filled in, so a widening that is plumbed through but never POPULATED — the record grown, the
/// column never selected — passes it and every other test in the suite. Only a run against the real
/// join can tell the two apart.
/// </para>
/// <para>
/// The agent is the single substitution, and only because it cannot be present: it is a separate
/// root process that reads the host's password database and walks home directories.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class MonitoringAccountDiskTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The password the seeded administrator is given.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The seeded plan's disk allowance, in megabytes — five gigabytes.</summary>
    private const int PlanDiskQuotaMb = 5_120;

    /// <summary>The same allowance in bytes, which is the unit the endpoint answers in.</summary>
    private const long PlanDiskQuotaBytes = PlanDiskQuotaMb * 1024L * 1024L;

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public MonitoringAccountDiskTests(PostgresFixture postgres)
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

    /// <summary>The agent's measurement arrives beside the allowance read from the accounts schema.</summary>
    /// <remarks>
    /// The assertion on <c>quotaBytes</c> is the one that proves the widening is real rather than
    /// merely compiling: 5,368,709,120 is the seeded plan's 5,120 MB, and it can only have reached
    /// the response by being selected out of the <c>Plans</c> table into the widened snapshot. A
    /// record grown but never populated answers 0 here.
    /// </remarks>
    [Fact]
    public async Task The_agents_measurement_arrives_beside_the_allowance_from_the_accounts_schema()
    {
        await using var factory = CreateFactory(Measuring(new AgentAccountDiskUsage("alice", 734_003_200)));
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/accounts-disk");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var alice = Row(body, "alice");

        Assert.Equal(734_003_200L, alice.GetProperty("usedBytes").GetInt64());
        Assert.Equal(PlanDiskQuotaBytes, alice.GetProperty("quotaBytes").GetInt64());
        Assert.Equal(5_368_709_120L, alice.GetProperty("quotaBytes").GetInt64());
    }

    /// <summary>An account the agent did not measure comes back with no figure, never a zero.</summary>
    /// <remarks>
    /// Asserted on the JSON value KIND rather than on a number, because that is the distinction the
    /// interface has to draw: <c>null</c> is "nobody measured this", <c>0</c> is "this account holds
    /// nothing", and a serializer that turned the first into the second would pass a numeric
    /// assertion.
    /// </remarks>
    [Fact]
    public async Task An_account_the_agent_did_not_measure_comes_back_with_no_figure()
    {
        await using var factory = CreateFactory(Measuring(new AgentAccountDiskUsage("alice", 734_003_200)));
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/accounts-disk");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var bob = Row(body, "bob");

        Assert.Equal(JsonValueKind.Null, bob.GetProperty("usedBytes").ValueKind);
        Assert.Equal(PlanDiskQuotaBytes, bob.GetProperty("quotaBytes").GetInt64());
    }

    /// <summary>A user the agent reports and the panel does not know is not listed as an account.</summary>
    /// <remarks>
    /// The agent reads the host's password database, so a name it reports that the panel never
    /// created is a system user. Listing it would invent a tenant, and its bytes are nobody's to be
    /// charged for.
    /// </remarks>
    [Fact]
    public async Task A_user_the_panel_does_not_know_is_not_listed_as_an_account()
    {
        await using var factory = CreateFactory(Measuring(
            new AgentAccountDiskUsage("alice", 10),
            new AgentAccountDiskUsage("backup", 999_999_999)));
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/accounts-disk");

        var payload = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(payload);

        Assert.Equal(2, body.RootElement.GetArrayLength());
        Assert.DoesNotContain("backup", payload, StringComparison.Ordinal);
    }

    /// <summary>A suspended account is still listed, because its files are still on the disk.</summary>
    /// <remarks>
    /// Omitting it would make the rows sum to less than the disk actually holds — and when somebody
    /// is looking at disk usage, the account suspended for filling it is often the one they came for.
    /// </remarks>
    [Fact]
    public async Task A_suspended_account_is_still_listed_because_its_files_are_still_there()
    {
        await using var factory = CreateFactory(Measuring(new AgentAccountDiskUsage("bob", 4_096)));
        await MigrateAsync(factory);
        await SeedAsync(factory, suspend: "bob");
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/accounts-disk");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(4_096L, Row(body, "bob").GetProperty("usedBytes").GetInt64());
    }

    /// <summary>An agent that will not answer fails the read rather than showing every account at zero.</summary>
    [Fact]
    public async Task An_agent_that_will_not_answer_fails_the_read()
    {
        var agent = new StubAgentMonitorClient
        {
            DiskUsage = Result<IReadOnlyList<AgentAccountDiskUsage>>.Fail(Error.Of("AgentUnavailable", ErrorType.Unavailable)),
        };
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/accounts-disk");

        // 503, because the failure is ErrorType.Unavailable and that is the only thing the status
        // is read from. It used to be 400 — the status was inferred from the code's spelling, no
        // suffix matched "AgentUnavailable", and the default told the administrator that the
        // request they had just made was malformed while the agent was the thing not answering.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AgentUnavailable", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>Finds one account's row in the response by its user name.</summary>
    /// <param name="body">The parsed response.</param>
    /// <param name="username">The account to find.</param>
    /// <returns>That account's row.</returns>
    private static JsonElement Row(JsonDocument body, string username)
    {
        return body.RootElement.EnumerateArray().Single(row =>
        {
            return row.GetProperty("username").GetString() == username;
        });
    }

    /// <summary>An agent double reporting exactly the given measurements.</summary>
    /// <param name="measured">What the agent found under each home directory.</param>
    /// <returns>The double.</returns>
    private static StubAgentMonitorClient Measuring(params AgentAccountDiskUsage[] measured)
    {
        return new StubAgentMonitorClient
        {
            DiskUsage = Result<IReadOnlyList<AgentAccountDiskUsage>>.Ok(measured),
        };
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the agent replaced.</summary>
    /// <param name="agent">The agent double this run answers from.</param>
    /// <returns>The booted host factory.</returns>
    private WebApplicationFactory<Program> CreateFactory(StubAgentMonitorClient agent)
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
    /// <remarks>
    /// Identity for the sign-in and Accounts for the join under test. No monitoring context: this
    /// endpoint stores nothing and reads no sample — it asks the agent and the accounts schema, and
    /// migrating a schema it never touches would suggest otherwise.
    /// </remarks>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds one plan, two accounts on it, and an administrator to read the view.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="suspend">The account to suspend, or <c>null</c> to leave both active.</param>
    private static async Task SeedAsync(WebApplicationFactory<Program> factory, string? suspend = null)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", PlanDiskQuotaMb, 5, 2, 3, 5, 5));

        var alice = new Account(Guid.NewGuid(), "alice", "alice.example.com", planId, now);
        var bob = new Account(Guid.NewGuid(), "bob", "bob.example.com", planId, now);
        if (suspend == "alice")
        {
            alice.Suspend();
        }
        else if (suspend == "bob")
        {
            bob.Suspend();
        }

        accounts.Accounts.AddRange(alice, bob);
        await accounts.SaveChangesAsync();

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
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
