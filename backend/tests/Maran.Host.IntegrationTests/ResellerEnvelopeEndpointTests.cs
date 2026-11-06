using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// What a reseller was sold is enforced on the server, over real HTTP against a real PostgreSQL
/// (issue #179): a plan over a ceiling is refused with the offending fields, the plan and account
/// counts and the disk pool refuse a write that would pass them — on creating an account, on moving one
/// to a bigger plan, on raising a plan — an envelope lowered below usage refuses new writes and edits
/// nothing, and two creations racing for one slot cannot both win.
/// </summary>
/// <remarks>
/// The default envelope (<see cref="ResellerEnvelopeSpec"/>) is three accounts, a 15 360 MB pool, three
/// plans and a 10 240 MB plan ceiling; alpha owns one 5 120 MB plan and one account on it, so alpha has
/// two account slots and 10 240 MB of pool left. Each test says which figure it tightens.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ResellerEnvelopeEndpointTests : IAsyncLifetime
{
    /// <summary>The two account names the racing creations ask for.</summary>
    private static readonly string[] RacerNames = { "racerone", "racertwo" };

    /// <summary>The feature a reseller's envelope does not sell, which a plan may therefore not include.</summary>
    private const string UnsoldFeature = "sites";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ResellerEnvelopeEndpointTests(PostgresFixture postgres)
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
    /// A plan one unit over a single ceiling, or leaving a capped runtime ceiling unlimited, or including
    /// a feature that was not sold, is refused with 400 <c>ResellerPlanExceedsEnvelope</c> naming exactly
    /// that field; nothing is stored and the refusal is journalled with the reseller as actor.
    /// </summary>
    /// <param name="kind">Which figure the plan breaks.</param>
    /// <param name="field">The field the refusal must name.</param>
    [Theory]
    [InlineData("diskQuotaMb", "diskQuotaMb")]
    [InlineData("maxSites", "maxSites")]
    [InlineData("maxDatabases", "maxDatabases")]
    [InlineData("maxSftpUsers", "maxSftpUsers")]
    [InlineData("maxFtpUsers", "maxFtpUsers")]
    [InlineData("maxCronEntries", "maxCronEntries")]
    [InlineData("maxPhpWorkersPerPool", "maxPhpWorkersPerPool")]
    [InlineData("cpuQuotaPercentOver", "cpuQuotaPercent")]
    [InlineData("cpuQuotaPercentUnlimited", "cpuQuotaPercent")]
    [InlineData("maxProcessesOver", "maxProcesses")]
    [InlineData("maxProcessesUnlimited", "maxProcesses")]
    [InlineData("features", "features")]
    public async Task A_plan_over_one_ceiling_is_refused_naming_that_field(string kind, string field)
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        var response = await alpha.PostAsJsonAsync("/api/v1/plans", PlanBreaking(kind));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ResellerPlanExceedsEnvelope", await ProblemDocument.CodeAsync(response));
        Assert.Equal([field], await ProblemDocument.EnvelopeFieldsAsync(response));
        Assert.Equal(1, await world.AccountsAsync(async accounts =>
        {
            return await accounts.Plans.IgnoreQueryFilters().CountAsync(plan => plan.ResellerId == world.Alpha.ResellerId);
        }));
        var refused = Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.PlanCreated));
        Assert.Equal(("alpha", "Over", false), (refused.ActorUsername, refused.Subject, refused.Succeeded));
    }

    /// <summary>
    /// A plan that breaks several ceilings at once is refused naming every one of them, so the reseller
    /// is told all it has to fix and not one thing at a time.
    /// </summary>
    [Fact]
    public async Task A_plan_over_several_ceilings_is_refused_naming_every_offending_field()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        var response = await alpha.PostAsJsonAsync(
            "/api/v1/plans",
            ResellerRequestBodies.Plan(
                "Over",
                diskQuotaMb: 20_000,
                maxSites: 99,
                features: [.. ResellerEnvelopeSpec.SoldFeatureNames, UnsoldFeature]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ["diskQuotaMb", "features", "maxSites"],
            (await ProblemDocument.EnvelopeFieldsAsync(response)).Order());
    }

    /// <summary>
    /// A plan exactly at every ceiling — the equal-to-limit boundary — is accepted, comes back owned by
    /// the reseller, and is journalled as a success.
    /// </summary>
    [Fact]
    public async Task A_plan_exactly_at_every_ceiling_is_accepted_and_owned_by_the_reseller()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        var response = await alpha.PostAsJsonAsync(
            "/api/v1/plans",
            ResellerRequestBodies.Plan(
                "Edge",
                diskQuotaMb: 10_240,
                maxSites: 10,
                maxDatabases: 5,
                maxSftpUsers: 4,
                maxFtpUsers: 2,
                maxCronEntries: 3,
                maxPhpWorkersPerPool: 8,
                cpuQuotaPercent: 200,
                maxProcesses: 128));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(world.Alpha.ResellerId, body.RootElement.GetProperty("resellerId").GetGuid());
        Assert.Equal("alpha", body.RootElement.GetProperty("resellerName").GetString());
        var created = Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.PlanCreated));
        Assert.Equal(("alpha", "Edge", true), (created.ActorUsername, created.Subject, created.Succeeded));
    }

    /// <summary>
    /// A reseller at its plan count is refused a further plan with 409 <c>ResellerPlanLimitReached</c>,
    /// after the plans inside the count were accepted; the refusal is journalled.
    /// </summary>
    [Fact]
    public async Task A_reseller_at_its_plan_limit_is_refused_a_further_plan()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        var second = await alpha.PostAsJsonAsync("/api/v1/plans", ResellerRequestBodies.Plan("PlanTwo"));
        var third = await alpha.PostAsJsonAsync("/api/v1/plans", ResellerRequestBodies.Plan("PlanThree"));
        var fourth = await alpha.PostAsJsonAsync("/api/v1/plans", ResellerRequestBodies.Plan("PlanFour"));

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(HttpStatusCode.Created, third.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, fourth.StatusCode);
        Assert.Equal("ResellerPlanLimitReached", await ProblemDocument.CodeAsync(fourth));
        Assert.Equal(3, await world.AccountsAsync(async accounts =>
        {
            return await accounts.Plans.IgnoreQueryFilters().CountAsync(plan => plan.ResellerId == world.Alpha.ResellerId);
        }));
        var entries = await world.AuditEntriesAsync(AccountAuditActions.PlanCreated);
        Assert.Equal(
            [("PlanTwo", true), ("PlanThree", true), ("PlanFour", false)],
            entries.Select(entry => { return (entry.Subject, entry.Succeeded); }));
    }

    /// <summary>
    /// A reseller sold two accounts, with one already, may create one more and is refused the next with
    /// 409 <c>ResellerAccountLimitReached</c> before the host is asked to create anything; the account it
    /// created is its own, its owner is invited by mail, and both outcomes are journalled.
    /// </summary>
    [Fact]
    public async Task A_reseller_at_its_account_limit_is_refused_a_further_account_before_the_host_is_asked()
    {
        await using var world = await new ResellerFixtureBuilder(_pg)
            .WithAlphaEnvelope(new ResellerEnvelopeSpec(MaxAccounts: 2))
            .BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        var second = await alpha.PostAsJsonAsync("/api/v1/accounts", ResellerRequestBodies.Account("alphatwo", world.Alpha.PlanId));
        var third = await alpha.PostAsJsonAsync("/api/v1/accounts", ResellerRequestBodies.Account("alphathree", world.Alpha.PlanId));

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        using var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(world.Alpha.ResellerId, body.RootElement.GetProperty("resellerId").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal("ResellerAccountLimitReached", await ProblemDocument.CodeAsync(third));
        Assert.Equal(["alphatwo"], world.Agent.Created);
        Assert.Equal(2, await world.AccountsAsync(async accounts =>
        {
            return await accounts.Accounts.IgnoreQueryFilters().CountAsync(account => account.ResellerId == world.Alpha.ResellerId);
        }));
        await SubUserTeamWorld.MailToAsync(world.Mailer, "alphatwo@example.com");
        Assert.DoesNotContain(world.Mailer.Sent, mail => { return mail.Recipient == "alphathree@example.com"; });
        var entries = await world.AuditEntriesAsync(AccountAuditActions.AccountCreated);
        Assert.Equal(
            [("alpha", "alphatwo", true), ("alpha", "alphathree", false)],
            entries.Select(entry => { return (entry.ActorUsername, entry.Subject, entry.Succeeded); }));
    }

    /// <summary>
    /// The disk pool admits an account whose plan brings the pool EXACTLY to its size and refuses the
    /// next one with 409 <c>ResellerDiskPoolExceeded</c> although an account slot is free.
    /// </summary>
    [Fact]
    public async Task The_disk_pool_admits_an_account_that_fills_it_exactly_and_refuses_the_next()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        var big = await CreatePlanAsync(alpha, "Big", diskQuotaMb: 10_240);

        var fills = await alpha.PostAsJsonAsync("/api/v1/accounts", ResellerRequestBodies.Account("alphatwo", big));
        var over = await alpha.PostAsJsonAsync("/api/v1/accounts", ResellerRequestBodies.Account("alphathree", world.Alpha.PlanId));

        Assert.Equal(HttpStatusCode.Created, fills.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        Assert.Equal("ResellerDiskPoolExceeded", await ProblemDocument.CodeAsync(over));
        Assert.Equal(["alphatwo"], world.Agent.Created);
        var entries = await world.AuditEntriesAsync(AccountAuditActions.AccountCreated);
        Assert.Equal([true, false], entries.Select(entry => { return entry.Succeeded; }));
    }

    /// <summary>
    /// Moving an account to a bigger plan is checked against the pool with the account's old disk
    /// released: a move that lands exactly on the pool is accepted and tells the host the new quota.
    /// </summary>
    [Fact]
    public async Task Moving_an_account_to_a_plan_that_fills_the_pool_exactly_is_accepted()
    {
        await using var world = await new ResellerFixtureBuilder(_pg)
            .WithAlphaEnvelope(new ResellerEnvelopeSpec(DiskPoolMb: 10_240))
            .BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        var big = await CreatePlanAsync(alpha, "Big", diskQuotaMb: 10_240);

        var response = await alpha.PutAsJsonAsync($"/api/v1/accounts/{world.Alpha.AccountId}/plan", new { PlanId = big });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(big, await StoredPlanOfAsync(world, world.Alpha.AccountId));
        Assert.Equal("alphaone", Assert.Single(world.Agent.Quotas).Username);
        var entry = Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.AccountPlanChanged));
        Assert.Equal(("alpha", "alphaone: AlphaPlan -> Big", true), (entry.ActorUsername, entry.Subject, entry.Succeeded));
    }

    /// <summary>
    /// Moving an account to a plan that would take the pool past its size is refused with 409
    /// <c>ResellerDiskPoolExceeded</c>; the account stays on its plan, the host is told nothing and the
    /// refusal is journalled.
    /// </summary>
    [Fact]
    public async Task Moving_an_account_to_a_plan_past_the_pool_is_refused_and_changes_nothing()
    {
        await using var world = await new ResellerFixtureBuilder(_pg)
            .WithAlphaEnvelope(new ResellerEnvelopeSpec(DiskPoolMb: 8_192))
            .BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        var big = await CreatePlanAsync(alpha, "Big", diskQuotaMb: 10_240);

        var response = await alpha.PutAsJsonAsync($"/api/v1/accounts/{world.Alpha.AccountId}/plan", new { PlanId = big });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ResellerDiskPoolExceeded", await ProblemDocument.CodeAsync(response));
        Assert.Equal(world.Alpha.PlanId, await StoredPlanOfAsync(world, world.Alpha.AccountId));
        Assert.Empty(world.Agent.Quotas);
        var entry = Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.AccountPlanChanged));
        Assert.Equal(("alpha", "alphaone: AlphaPlan -> Big", false), (entry.ActorUsername, entry.Subject, entry.Succeeded));
    }

    /// <summary>
    /// Raising a plan's disk is checked against the pool for every account already on it: a raise that
    /// would take the pool past its size is refused with 409 <c>ResellerDiskPoolExceeded</c> and the plan
    /// keeps its figure; a raise that fits is accepted. Without this a reseller could exceed its pool by
    /// editing a plan after the accounts exist.
    /// </summary>
    /// <param name="poolMb">The pool the reseller was sold.</param>
    /// <param name="accepted">Whether the raise to 10 240 MB fits that pool.</param>
    [Theory]
    [InlineData(8_192L, false)]
    [InlineData(10_240L, true)]
    public async Task Raising_a_plans_disk_is_checked_against_the_pool_for_every_account_on_it(
        long poolMb,
        bool accepted)
    {
        await using var world = await new ResellerFixtureBuilder(_pg)
            .WithAlphaEnvelope(new ResellerEnvelopeSpec(DiskPoolMb: poolMb))
            .BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        var response = await alpha.PutAsJsonAsync(
            $"/api/v1/plans/{world.Alpha.PlanId}",
            ResellerRequestBodies.Plan("AlphaPlan", diskQuotaMb: 10_240));

        var storedDisk = await world.AccountsAsync(async accounts =>
        {
            return await accounts.Plans.IgnoreQueryFilters().Where(plan => plan.Id == world.Alpha.PlanId).Select(plan => plan.DiskQuotaMb).SingleAsync();
        });
        var entry = Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.PlanUpdated));
        Assert.Equal(("alpha", accepted), (entry.ActorUsername, entry.Succeeded));
        if (accepted)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(10_240, storedDisk);
            return;
        }

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ResellerDiskPoolExceeded", await ProblemDocument.CodeAsync(response));
        Assert.Equal(5_120, storedDisk);
    }

    /// <summary>
    /// An envelope an administrator lowers below what the reseller owns refuses every new account and
    /// plan with its own code, edits nothing the reseller already has, and leaves what it owns readable.
    /// </summary>
    [Fact]
    public async Task An_envelope_lowered_below_usage_refuses_new_writes_and_edits_nothing_that_exists()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");
        using var alpha = await world.SignInAsync("alpha");
        var lowered = await admin.PutAsJsonAsync(
            $"/api/v1/resellers/{world.Alpha.ResellerId}/envelope",
            new { Envelope = new ResellerEnvelopeSpec(MaxAccounts: 0, DiskPoolMb: 0, MaxPlans: 0).ToBody() });
        Assert.Equal(HttpStatusCode.OK, lowered.StatusCode);

        var account = await alpha.PostAsJsonAsync("/api/v1/accounts", ResellerRequestBodies.Account("alphatwo", world.Alpha.PlanId));
        var plan = await alpha.PostAsJsonAsync("/api/v1/plans", ResellerRequestBodies.Plan("Gamma"));

        Assert.Equal("ResellerAccountLimitReached", await ProblemDocument.CodeAsync(account));
        Assert.Equal("ResellerPlanLimitReached", await ProblemDocument.CodeAsync(plan));
        Assert.Equal(HttpStatusCode.OK, (await alpha.GetAsync($"/api/v1/accounts/{world.Alpha.AccountId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alpha.GetAsync($"/api/v1/plans/{world.Alpha.PlanId}")).StatusCode);
        Assert.Equal(5_120, await world.AccountsAsync(async accounts =>
        {
            return await accounts.Plans.IgnoreQueryFilters().Where(p => p.Id == world.Alpha.PlanId).Select(p => p.DiskQuotaMb).SingleAsync();
        }));
        Assert.Empty(world.Agent.Created);
    }

    /// <summary>
    /// Two account creations that both pass the count while one slot is left cannot both succeed: exactly
    /// one is created, the other is told the limit was reached, the system user the loser made on the
    /// host is removed again, and the reseller ends with exactly the accounts it was sold.
    /// </summary>
    /// <remarks>
    /// How the test is made able to LOSE: both creations are held inside the agent call until both have
    /// arrived there, which is to say until both have passed the admission check believing there was
    /// room. Without that the two requests could be serialised by chance and the test would pass over a
    /// guard that guards nothing. The creation count at the agent is asserted for that reason.
    /// </remarks>
    [Fact]
    public async Task Two_creations_racing_for_the_last_account_slot_cannot_both_succeed()
    {
        var agent = new ResellerAgentAccountsClient(expectedCreations: 2);
        await using var world = await new ResellerFixtureBuilder(_pg)
            .WithAgent(agent)
            .WithAlphaEnvelope(new ResellerEnvelopeSpec(MaxAccounts: 2))
            .BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        // Warm the host before the race. The message handlers are compiled at run time, per host, on the first
        // call into each command chain, and a race that is also the first call can see both requests fail before
        // either reaches the agent: observed in CI as `Created.Count` 0 after one second, with no statuses to read.
        // An unknown plan makes the handler answer without taking a slot or calling the agent.
        using var warmUp = await alpha.PostAsJsonAsync("/api/v1/accounts", ResellerRequestBodies.Account("warmup", Guid.NewGuid()));
        Assert.True((int)warmUp.StatusCode < 500, $"The warm-up request answered {(int)warmUp.StatusCode}.");

        var responses = await Task.WhenAll(
            alpha.PostAsJsonAsync("/api/v1/accounts", ResellerRequestBodies.Account("racerone", world.Alpha.PlanId)),
            alpha.PostAsJsonAsync("/api/v1/accounts", ResellerRequestBodies.Account("racertwo", world.Alpha.PlanId)));

        // Both racers must have reached the agent for the race to have happened at all. When they did not, the
        // answers say why, which a bare count never did.
        var answers = string.Join(
            " | ",
            await Task.WhenAll(responses.Select(async response =>
            {
                return $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}";
            })));
        Assert.True(agent.Created.Count == 2, $"Both creations should have reached the agent; they answered: {answers}");
        var winners = responses.Where(response => { return response.StatusCode == HttpStatusCode.Created; }).ToList();
        var losers = responses.Where(response => { return response.StatusCode == HttpStatusCode.Conflict; }).ToList();
        var winner = Assert.Single(winners);
        var loser = Assert.Single(losers);
        Assert.Equal("ResellerAccountLimitReached", await ProblemDocument.CodeAsync(loser));
        using var winnerBody = JsonDocument.Parse(await winner.Content.ReadAsStringAsync());
        var winnerName = winnerBody.RootElement.GetProperty("name").GetString();
        var loserName = RacerNames.Single(name => { return name != winnerName; });
        Assert.Equal(2, await world.AccountsAsync(async accounts =>
        {
            return await accounts.Accounts.IgnoreQueryFilters().CountAsync(account => account.ResellerId == world.Alpha.ResellerId);
        }));
        Assert.Equal(0, await world.AccountsAsync(async accounts =>
        {
            return await accounts.Accounts.IgnoreQueryFilters().CountAsync(account => account.Name == loserName);
        }));
        Assert.Equal([loserName], agent.Deleted);
    }

    /// <summary>The body of a plan that breaks exactly one figure of the default envelope.</summary>
    /// <param name="kind">Which figure to break.</param>
    /// <returns>The request body.</returns>
    private static object PlanBreaking(string kind)
    {
        return kind switch
        {
            "diskQuotaMb" => ResellerRequestBodies.Plan("Over", diskQuotaMb: 10_241),
            "maxSites" => ResellerRequestBodies.Plan("Over", maxSites: 11),
            "maxDatabases" => ResellerRequestBodies.Plan("Over", maxDatabases: 6),
            "maxSftpUsers" => ResellerRequestBodies.Plan("Over", maxSftpUsers: 5),
            "maxFtpUsers" => ResellerRequestBodies.Plan("Over", maxFtpUsers: 3),
            "maxCronEntries" => ResellerRequestBodies.Plan("Over", maxCronEntries: 4),
            "maxPhpWorkersPerPool" => ResellerRequestBodies.Plan("Over", maxPhpWorkersPerPool: 9),
            "cpuQuotaPercentOver" => ResellerRequestBodies.Plan("Over", cpuQuotaPercent: 201),
            "cpuQuotaPercentUnlimited" => ResellerRequestBodies.Plan("Over", cpuQuotaPercent: null),
            "maxProcessesOver" => ResellerRequestBodies.Plan("Over", maxProcesses: 129),
            "maxProcessesUnlimited" => ResellerRequestBodies.Plan("Over", maxProcesses: null),
            "features" => ResellerRequestBodies.Plan("Over", features: [.. ResellerEnvelopeSpec.SoldFeatureNames, UnsoldFeature]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No such figure to break."),
        };
    }

    /// <summary>Creates a plan as the signed-in reseller and returns its id.</summary>
    /// <param name="client">The reseller's client.</param>
    /// <param name="name">The plan's name.</param>
    /// <param name="diskQuotaMb">The plan's disk quota, in MB.</param>
    /// <returns>The new plan's id.</returns>
    private static async Task<Guid> CreatePlanAsync(HttpClient client, string name, int diskQuotaMb)
    {
        var response = await client.PostAsJsonAsync("/api/v1/plans", ResellerRequestBodies.Plan(name, diskQuotaMb: diskQuotaMb));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Reads the plan an account is stored on.</summary>
    /// <param name="world">The seeded panel.</param>
    /// <param name="accountId">The account.</param>
    /// <returns>The plan's id.</returns>
    private static async Task<Guid> StoredPlanOfAsync(ResellerWorld world, Guid accountId)
    {
        return await world.AccountsAsync(async accounts =>
        {
            return await accounts.Accounts.IgnoreQueryFilters().Where(account => account.Id == accountId)
                .Select(account => account.PlanId)
                .SingleAsync();
        });
    }
}
