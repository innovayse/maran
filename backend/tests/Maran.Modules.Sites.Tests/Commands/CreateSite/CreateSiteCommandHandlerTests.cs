using Maran.Modules.Sites.Commands.CreateSite;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.Commands.CreateSite;

/// <summary>
/// Behavioral contract of <see cref="CreateSiteCommandHandler"/>. Runs against a real
/// <see cref="SitesDbContext"/> backed by the EF Core InMemory provider — the handler's own
/// dependency — so the limit count, the uniqueness check and the tenant filter are exercised as
/// written; each test gets its own database (rules/testing.md "Determinism").
/// </summary>
public sealed class CreateSiteCommandHandlerTests
{
    /// <summary>A fixed instant, so nothing here reads the ambient clock.</summary>
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Creating a site provisions it on the host and then records the row.</summary>
    [Fact]
    public async Task Creating_a_site_provisions_it_on_the_host_and_then_records_the_row()
    {
        var account = Guid.NewGuid();
        var world = new World(account);

        var result = await world.Handler().HandleAsync(Command(account), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("example.com", result.Value.Domain);
        Assert.Equal(SiteStatus.Enabled, result.Value.Status);
        Assert.Equal(Now, result.Value.CreatedAt);
        // The trailing 10 is the plan's per-pool worker budget, which travels with the creation
        // because the agent writes the site's php-fpm pool as part of creating it. Without it the
        // pool is written with a pm.max_children of zero, which php-fpm refuses to start.
        Assert.Equal("create:acme:example.com:Php:8.3:www.example.com:10", Assert.Single(world.Agent.Calls));

        var stored = await world.DbContext.Sites.SingleAsync();
        Assert.Equal("/home/acme/sites/example.com", stored.DocumentRoot);
        Assert.False(stored.HasCertificate);
    }

    /// <summary>Creating a site journals the domain as the subject.</summary>
    [Fact]
    public async Task Creating_a_site_journals_the_domain_as_the_subject()
    {
        var account = Guid.NewGuid();
        var world = new World(account);

        await world.Handler().HandleAsync(Command(account), CancellationToken.None);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.SiteCreated, entry.Action);
        Assert.Equal("example.com", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A site beyond the plans allowance is refused before the agent is called at all.</summary>
    [Fact]
    public async Task A_site_beyond_the_plans_allowance_is_refused_before_the_agent_is_called_at_all()
    {
        var account = Guid.NewGuid();
        var world = new World(account, maxSites: 1);
        world.DbContext.Sites.Add(SitesTestContext.PhpSite(account, "first.example.com"));
        await world.DbContext.SaveChangesAsync();

        var result = await world.Handler().HandleAsync(Command(account), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteLimitReached", result.Error!.Code);

        // Spec §8: a site the plan refuses must never reach the host. An empty call list is the
        // assertion — a limit checked after provisioning would leave a vhost nobody asked for.
        Assert.Empty(world.Agent.Calls);
        Assert.Equal(1, await world.DbContext.Sites.CountAsync());
    }

    /// <summary>A domain already served for another tenant is still taken.</summary>
    [Fact]
    public async Task A_domain_already_served_for_another_tenant_is_still_taken()
    {
        // The uniqueness check must ignore the tenant filter: a domain is claimed once per SERVER.
        // With the filter applied the conflicting row would be invisible, the check would pass, and
        // the insert would blow up on the unique index instead of answering a typed 409.
        var account = Guid.NewGuid();
        var world = new World(account);
        await using (var otherTenant = SitesTestContext.Create(FakeCurrentUser.Admin(), world.Database))
        {
            otherTenant.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "example.com"));
            await otherTenant.SaveChangesAsync();
        }

        var result = await world.Handler().HandleAsync(Command(account), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteDomainTaken", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>An alias naming another tenants domain is refused.</summary>
    [Fact]
    public async Task An_alias_naming_another_tenants_domain_is_refused()
    {
        // The domain takeover this uniqueness exists to stop. nginx resolves a request by Host
        // alone, and the include is a sorted glob, so a site whose file sorts first wins every
        // request for a name it claims — the victim's ACME challenge location included, which is a
        // publicly trusted certificate for a domain the requester does not own.
        var account = Guid.NewGuid();
        var world = new World(account);
        await using (var otherTenant = SitesTestContext.Create(FakeCurrentUser.Admin(), world.Database))
        {
            otherTenant.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "victim.example.com"));
            await otherTenant.SaveChangesAsync();
        }

        var result = await world.Handler().HandleAsync(
            Command(account, "aaa-attacker.example.com", ["victim.example.com"]), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteDomainTaken", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>An alias naming another tenants alias is refused.</summary>
    [Fact]
    public async Task An_alias_naming_another_tenants_alias_is_refused()
    {
        // The same takeover one step further out: two vhosts claiming one name is a takeover
        // whether the name is the victim's primary domain or one of its aliases, because nginx
        // makes no distinction between the two in server_name.
        var account = Guid.NewGuid();
        var world = new World(account);
        await using (var otherTenant = SitesTestContext.Create(FakeCurrentUser.Admin(), world.Database))
        {
            otherTenant.Sites.Add(
                SitesTestContext.PhpSite(Guid.NewGuid(), "victim.example.com", "8.3", "shop.example.com"));
            await otherTenant.SaveChangesAsync();
        }

        var result = await world.Handler().HandleAsync(
            Command(account, "aaa-attacker.example.com", ["shop.example.com"]), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteDomainTaken", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>A domain naming another tenants alias is refused.</summary>
    [Fact]
    public async Task A_domain_naming_another_tenants_alias_is_refused()
    {
        // The direction the Domain unique index alone cannot see: the conflicting claim is not
        // another site's Domain column, so only a check over the whole claimed set catches it.
        var account = Guid.NewGuid();
        var world = new World(account);
        await using (var otherTenant = SitesTestContext.Create(FakeCurrentUser.Admin(), world.Database))
        {
            otherTenant.Sites.Add(
                SitesTestContext.PhpSite(Guid.NewGuid(), "victim.example.com", "8.3", "shop.example.com"));
            await otherTenant.SaveChangesAsync();
        }

        var result = await world.Handler().HandleAsync(
            Command(account, "shop.example.com", []), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteDomainTaken", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>An alias differing only in case from another tenants domain is refused.</summary>
    [Fact]
    public async Task An_alias_differing_only_in_case_from_another_tenants_domain_is_refused()
    {
        // Host matching is case-insensitive, so "Victim.example.com" and "victim.example.com" are
        // one name to nginx and must be one claim here.
        var account = Guid.NewGuid();
        var world = new World(account);
        await using (var otherTenant = SitesTestContext.Create(FakeCurrentUser.Admin(), world.Database))
        {
            otherTenant.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "victim.example.com"));
            await otherTenant.SaveChangesAsync();
        }

        var result = await world.Handler().HandleAsync(
            Command(account, "aaa-attacker.example.com", ["Victim.Example.com"]), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteDomainTaken", result.Error!.Code);
    }

    /// <summary>A created site claims its domain and every alias.</summary>
    [Fact]
    public async Task A_created_site_claims_its_domain_and_every_alias()
    {
        // The claims are what the next creation is refused against, so a site stored without them
        // would leave every name it serves free for another account to take.
        var account = Guid.NewGuid();
        var world = new World(account);

        await world.Handler().HandleAsync(
            Command(account, "example.com", ["www.example.com", "shop.example.com"]), CancellationToken.None);

        var claimed = await world.DbContext.SiteHostnames
            .IgnoreQueryFilters()
            .Select(hostname => hostname.Name)
            .ToListAsync();
        Assert.Equal(
            ["example.com", "shop.example.com", "www.example.com"],
            claimed.OrderBy(
                name =>
                {
                    return name;
                },
                StringComparer.Ordinal));
    }

    /// <summary>A php version the host does not have is refused before the agent is asked to create anything.</summary>
    [Fact]
    public async Task A_php_version_the_host_does_not_have_is_refused_before_the_agent_is_asked_to_create_anything()
    {
        var account = Guid.NewGuid();
        var world = new World(account, installedPhp: ["8.2"]);

        var result = await world.Handler().HandleAsync(Command(account), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PhpVersionNotInstalled", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>An agent that cannot list versions is not reported as a missing version.</summary>
    [Fact]
    public async Task An_agent_that_cannot_list_versions_is_not_reported_as_a_missing_version()
    {
        // The two are different answers: one is retried, the other is a fact the customer must act
        // on. Collapsing them would tell a customer their PHP version is uninstalled every time the
        // agent socket hiccups.
        var account = Guid.NewGuid();
        var world = new World(account, phpFailure: Error.Of("AgentUnavailable"));

        var result = await world.Handler().HandleAsync(Command(account), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
    }

    /// <summary>A static site is created without consulting the php runtimes at all.</summary>
    [Fact]
    public async Task A_static_site_is_created_without_consulting_the_php_runtimes_at_all()
    {
        var account = Guid.NewGuid();
        var world = new World(account, installedPhp: []);

        var result = await world.Handler().HandleAsync(
            Command(account) with { BackendType = SiteBackendType.Static, PhpVersion = string.Empty },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, world.Php.ListCalls);
    }

    /// <summary>A refused provisioning leaves no site row behind.</summary>
    [Fact]
    public async Task A_refused_provisioning_leaves_no_site_row_behind()
    {
        // The agent runs before the row precisely so this is the failure mode: nothing was written,
        // and the caller sees the agent's own typed error rather than a site that does not serve.
        var account = Guid.NewGuid();
        var world = new World(account, agentFailure: Error.Of("AgentValidationFailed"));

        var result = await world.Handler().HandleAsync(Command(account), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentValidationFailed", result.Error!.Code);
        Assert.Empty(await world.DbContext.Sites.ToListAsync());

        // The refusal IS journalled — it is the half of the journal worth reading (AuditEntry).
        // What must not exist is a SUCCESS entry for a site that was never created.
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>Creating a site for an account the caller does not own answers not found.</summary>
    [Fact]
    public async Task Creating_a_site_for_an_account_the_caller_does_not_own_answers_not_found()
    {
        // The directory answers null for another tenant's account, exactly as it answers for one
        // that does not exist — so the response cannot be used to discover which account ids exist.
        var account = Guid.NewGuid();
        var world = new World(account);

        var result = await world.Handler().HandleAsync(Command(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>The plan limit counts the owning accounts sites when an administrator creates one.</summary>
    [Fact]
    public async Task The_plan_limit_counts_the_owning_accounts_sites_when_an_administrator_creates_one()
    {
        // The case the count predicate exists for. An administrator is not narrowed by the tenant
        // filter, so if the count ever stopped scoping itself to the account being created for, an
        // admin-issued create would compare the WHOLE SERVER against one customer plan — refusing a
        // customer their second site because somebody else has five.
        var account = Guid.NewGuid();
        var world = new World(account, maxSites: 2, asAdministrator: true);
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), world.Database))
        {
            seed.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "someone-else-1.example.com"));
            seed.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "someone-else-2.example.com"));
            seed.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "someone-else-3.example.com"));
            await seed.SaveChangesAsync();
        }

        var result = await world.Handler().HandleAsync(Command(account), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>The plan limit counts only the owning accounts sites.</summary>
    [Fact]
    public async Task The_plan_limit_counts_only_the_owning_accounts_sites()
    {
        // The other direction: the account is genuinely at its limit, and reaching it must not
        // depend on who is asking.
        var account = Guid.NewGuid();
        var world = new World(account, maxSites: 1, asAdministrator: true);
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), world.Database))
        {
            seed.Sites.Add(SitesTestContext.PhpSite(account, "already.example.com"));
            await seed.SaveChangesAsync();
        }

        var result = await world.Handler().HandleAsync(Command(account), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteLimitReached", result.Error!.Code);
    }

    /// <summary>The stored site keeps the aliases it was created with.</summary>
    [Fact]
    public async Task The_stored_site_keeps_the_aliases_it_was_created_with()
    {
        // Asserted on the ROW, not only on the agent argument. The row is what every later
        // re-render reads (SiteDescriptorFactory), so aliases dropped on the way into the database
        // would survive creation intact and then vanish from the vhost on the first enable, disable
        // or version change — long after anyone would connect the two.
        var account = Guid.NewGuid();
        var world = new World(account);

        await world.Handler().HandleAsync(Command(account), CancellationToken.None);

        var stored = await world.DbContext.Sites.SingleAsync();
        Assert.Equal(["www.example.com"], stored.Aliases);
    }

    /// <summary>The requests cancellation token reaches the agent.</summary>
    [Fact]
    public async Task The_requests_cancellation_token_reaches_the_agent()
    {
        var account = Guid.NewGuid();
        var world = new World(account);
        using var cancellation = new CancellationTokenSource();

        await world.Handler().HandleAsync(Command(account), cancellation.Token);

        Assert.Equal(cancellation.Token, Assert.Single(world.Agent.Tokens));
    }

    /// <summary>A refused creation is journalled as a failure.</summary>
    [Fact]
    public async Task A_refused_creation_is_journalled_as_a_failure()
    {
        // AuditEntry says failures are the half of the journal worth reading, and a refused
        // provisioning is exactly the event an operator later needs to explain a missing site.
        var account = Guid.NewGuid();
        var world = new World(account, agentFailure: Error.Of("AgentSystemFailure"));

        await world.Handler().HandleAsync(Command(account), CancellationToken.None);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal(AuditActions.SiteCreated, entry.Action);
        Assert.Equal("example.com", entry.Subject);
    }

    /// <summary>A creation refused by the plan limit is journalled as a failure.</summary>
    [Fact]
    public async Task A_creation_refused_by_the_plan_limit_is_journalled_as_a_failure()
    {
        var account = Guid.NewGuid();
        var world = new World(account, maxSites: 1);
        world.DbContext.Sites.Add(SitesTestContext.PhpSite(account, "first.example.com"));
        await world.DbContext.SaveChangesAsync();

        await world.Handler().HandleAsync(Command(account), CancellationToken.None);

        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>The journal names the caller who made the change.</summary>
    [Fact]
    public async Task The_journal_names_the_caller_who_made_the_change()
    {
        // Both halves: an id nobody can read is not an answer to "who did what", and a blank name
        // is what every Sites entry carried before ICurrentUser exposed one.
        var account = Guid.NewGuid();
        var world = new World(account);

        await world.Handler().HandleAsync(Command(account), CancellationToken.None);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(world.CurrentUser.UserId, entry.ActorUserId);
        Assert.Equal(world.CurrentUser.Username, entry.ActorUsername);
        Assert.NotEqual(string.Empty, entry.ActorUsername);
    }

    /// <summary>Builds the command under test.</summary>
    /// <param name="accountId">The account the site is created for.</param>
    /// <param name="domain">The primary domain requested.</param>
    /// <param name="aliases">The additional hostnames requested.</param>
    private static CreateSiteCommand Command(
        Guid accountId,
        string domain = "example.com",
        string[]? aliases = null)
    {
        return new CreateSiteCommand(
            accountId,
            domain,
            aliases ?? ["www.example.com"],
            SiteBackendType.Php,
            "8.3",
            string.Empty,
            "198.51.100.7",
            "tests");
    }

    /// <summary>Everything one handler run needs, assembled once so each test states only its own variation.</summary>
    private sealed class World
    {
        /// <summary>The in-memory database shared by every context this world hands out.</summary>
        public string Database { get; }

        /// <summary>The context the handler is given.</summary>
        public SitesDbContext DbContext { get; }

        /// <summary>The agent double.</summary>
        public RecordingAgentSitesClient Agent { get; }

        /// <summary>The PHP runtime double.</summary>
        public RecordingAgentPhpClient Php { get; }

        /// <summary>The account directory double.</summary>
        public StubAccountDirectory Accounts { get; }

        /// <summary>The audit journal double.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>The principal the context and the handler share.</summary>
        public FakeCurrentUser CurrentUser { get; }

        /// <summary>Assembles the world.</summary>
        /// <param name="accountId">The account the caller owns.</param>
        /// <param name="maxSites">The plan's site allowance.</param>
        /// <param name="installedPhp">Versions the host reports as installed.</param>
        /// <param name="agentFailure">When set, the agent refuses every site operation.</param>
        /// <param name="phpFailure">When set, the agent cannot list PHP versions.</param>
        /// <param name="asAdministrator">When true the caller is an administrator, whom no tenant filter narrows.</param>
        public World(
            Guid accountId,
            int maxSites = 5,
            string[]? installedPhp = null,
            Error? agentFailure = null,
            Error? phpFailure = null,
            bool asAdministrator = false)
        {
            Database = Guid.NewGuid().ToString();
            CurrentUser = asAdministrator ? FakeCurrentUser.Admin() : FakeCurrentUser.Customer(accountId);
            DbContext = SitesTestContext.Create(CurrentUser, Database);
            Agent = agentFailure is null ? new RecordingAgentSitesClient() : new RecordingAgentSitesClient(agentFailure);
            Php = phpFailure is null
                ? new RecordingAgentPhpClient(installedPhp ?? ["8.3"])
                : new RecordingAgentPhpClient(phpFailure);
            Accounts = new StubAccountDirectory(new AccountSnapshot(
                accountId,
                "acme",
                maxSites,
                MaxDatabases: 2,
                MaxSftpUsers: 3,
                MaxPhpWorkersPerPool: 10));
        }

        /// <summary>Builds the handler under test.</summary>
        public CreateSiteCommandHandler Handler()
        {
            return new CreateSiteCommandHandler(
                DbContext, Accounts, Agent, Php, new SiteAuditJournal(Audit, CurrentUser), new FakeClock(Now));
        }
    }
}
