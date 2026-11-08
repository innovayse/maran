using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AccountsService;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The options of <c>POST /api/v1/accounts</c> added in issue #194, over real HTTP against a real
/// PostgreSQL: the body names the SPA sends reach the agent and the stored row, the refusals carry
/// their codes and reach no agent, and the plan picker offers the process ceiling the form starts from.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class AccountOptionsEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AccountOptionsEndpointTests(PostgresFixture postgres)
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
        return Task.CompletedTask;
    }

    /// <summary>
    /// The ceilings and shell an administrator sends are provisioned, pushed and stored:
    /// the inode ceiling and the shell travel with the creation, the open-files ceiling follows it, and
    /// the row keeps all of it for the plan changes and reactivations that come later.
    /// </summary>
    [Fact]
    public async Task The_options_an_administrator_sends_reach_the_agent_and_the_stored_row()
    {
        var agent = new RecordingCreateAgentAccountsClient();
        await using var factory = CreateFactory(agent);
        var (client, planId) = await SignedInWithAPlanAsync(factory);
        using var admin = client;

        var response = await admin.PostAsJsonAsync("/api/v1/accounts", new
        {
            Name = "acme",
            PrimaryDomain = "acme.example.com",
            PlanId = planId,
            OwnerEmail = "owner@example.com",
            InodeLimit = 50_000,
            ProcessLimit = 500,
            OpenFilesLimit = 2_048,
            ShellAccess = true,
            BackupEnabled = false,
        });

        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var provisioning = Assert.Single(agent.Provisioned);
        Assert.Equal(50_000UL, provisioning.InodeLimit);
        Assert.Equal(AccountLoginShell.Bash, provisioning.Shell);
        Assert.Equal(("acme", (ulong?)2_048UL), Assert.Single(agent.OpenFilesLimits));
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var stored = await context.Accounts.IgnoreQueryFilters().SingleAsync(account => account.Name == "acme");
        Assert.True(stored.ShellAccess);
        Assert.Equal(50_000, stored.LimitOverrides.InodeLimit);
        Assert.Equal(500, stored.LimitOverrides.ProcessLimit);
        Assert.Equal(2_048, stored.LimitOverrides.OpenFilesLimit);
    }

    /// <summary>
    /// A body that states nothing of the options still creates the account the way it always was: no
    /// ceiling, no shell and no open-files push. The control for the case above.
    /// </summary>
    [Fact]
    public async Task A_body_that_states_none_of_the_options_creates_the_account_the_way_it_always_was()
    {
        var agent = new RecordingCreateAgentAccountsClient();
        await using var factory = CreateFactory(agent);
        var (client, planId) = await SignedInWithAPlanAsync(factory);
        using var admin = client;

        var response = await admin.PostAsJsonAsync("/api/v1/accounts", Body(planId));

        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var provisioning = Assert.Single(agent.Provisioned);
        Assert.Null(provisioning.InodeLimit);
        Assert.Equal(AccountLoginShell.Nologin, provisioning.Shell);
        Assert.Empty(agent.OpenFilesLimits);
    }

    /// <summary>
    /// Each refusal of the options carries its machine code with a 400 and reaches no agent: the
    /// initial password mode without a password, a password beside an invitation, and a figure outside
    /// the agent's range for each of the three ceilings.
    /// </summary>
    /// <param name="field">The member to break.</param>
    /// <param name="value">The value to give it, as JSON.</param>
    /// <param name="code">The refusal expected.</param>
    [Theory]
    [InlineData("loginMode", "\"initialPassword\"", "AccountInitialPasswordInvalid")]
    [InlineData("initialPassword", "\"Correct-Horse-9\"", "AccountInitialPasswordInvalid")]
    [InlineData("processLimit", "0", "AccountProcessLimitInvalid")]
    [InlineData("inodeLimit", "4294967296", "AccountInodeLimitInvalid")]
    [InlineData("openFilesLimit", "1048577", "AccountOpenFilesLimitInvalid")]
    public async Task An_option_outside_its_rules_is_refused_with_its_code_and_reaches_no_agent(
        string field, string value, string code)
    {
        var agent = new RecordingCreateAgentAccountsClient();
        await using var factory = CreateFactory(agent);
        var (client, planId) = await SignedInWithAPlanAsync(factory);
        using var admin = client;
        var json = $$"""
            {"name":"acme","primaryDomain":"acme.example.com","planId":"{{planId}}","ownerEmail":"owner@example.com","{{field}}":{{value}}}
            """;

        var response = await admin.PostAsync("/api/v1/accounts", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        Assert.Empty(agent.Created);
    }

    /// <summary>The plan picker offers each plan's process ceiling as <c>kind</c> and, beside a cap, <c>maximum</c>.</summary>
    [Fact]
    public async Task The_plan_picker_offers_each_plans_process_ceiling()
    {
        await using var factory = CreateFactory(new RecordingCreateAgentAccountsClient());
        var (client, _) = await SignedInWithAPlanAsync(factory);
        using var admin = client;

        var response = await admin.GetAsync("/api/v1/plans/choices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ceiling = body.RootElement.GetProperty("items")[0].GetProperty("maxProcesses");
        Assert.Equal("unlimited", ceiling.GetProperty("kind").GetString());
    }

    /// <summary>A creation request body whose only variable is the plan.</summary>
    /// <param name="planId">An existing plan.</param>
    /// <returns>The body.</returns>
    private static object Body(Guid planId)
    {
        return new
        {
            Name = "acme",
            PrimaryDomain = "acme.example.com",
            PlanId = planId,
            OwnerEmail = "owner@example.com",
        };
    }

    /// <summary>Boots the host with the recording agent in place of the real one.</summary>
    /// <param name="agent">The recording agent.</param>
    /// <returns>The factory, which the caller disposes.</returns>
    private WebApplicationFactory<Program> CreateFactory(RecordingCreateAgentAccountsClient agent)
    {
        return EndpointHarness.CreateFactory(_pg, services =>
        {
            services.RemoveAll<IAgentAccountsClient>();
            services.AddSingleton<IAgentAccountsClient>(agent);
        });
    }

    /// <summary>Migrates, seeds an administrator and a plan, and signs the administrator in.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The administrator's client and the plan's id.</returns>
    private static async Task<(HttpClient Admin, Guid PlanId)> SignedInWithAPlanAsync(
        WebApplicationFactory<Program> factory)
    {
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (planId, _) = await EndpointHarness.SeedAccountAsync(factory, "seeded");

        return (await EndpointHarness.SignInAsync(factory, "admin"), planId);
    }
}
