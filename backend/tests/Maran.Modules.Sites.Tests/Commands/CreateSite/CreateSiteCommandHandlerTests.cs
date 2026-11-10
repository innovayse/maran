using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Commands.CreateSite;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Apps;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.Commands.CreateSite;

/// <summary>
/// Behavioral contract of <see cref="CreateSiteCommandHandler"/> for the ordinary path: what a
/// creation stores and provisions, the plan limit, the PHP runtime check, the account check and the
/// audit entry it writes. Runs against a real <see cref="SitesDbContext"/> backed by the EF Core
/// InMemory provider so the limit count and the tenant filter are exercised as written; each test
/// gets its own database (rules/testing.md "Determinism"). Hostname claims are in
/// <see cref="CreateSiteCommandHandlerHostnameClaimTests"/> and the compensation after a lost race
/// in <see cref="CreateSiteCommandHandlerCompensationTests"/>.
/// </summary>
public sealed class CreateSiteCommandHandlerTests
{
    /// <inheritdoc/>
    [Fact]
    public async Task Creating_a_site_provisions_it_on_the_host_and_then_records_the_row()
    {
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("example.com", result.Value.Domain);
        Assert.Equal(SiteStatus.Enabled, result.Value.Status);
        Assert.Equal(CreateSiteWorld.Now, result.Value.CreatedAt);
        // The trailing 10 is the plan's per-pool worker budget, which travels with the creation
        // because the agent writes the site's php-fpm pool as part of creating it. Without it the
        // pool is written with a pm.max_children of zero, which php-fpm refuses to start.
        Assert.Equal("create:acme:example.com:Php:8.3:www.example.com:10", Assert.Single(world.Agent.Calls));

        var stored = await world.DbContext.Sites.SingleAsync();
        Assert.Equal("/home/acme/sites/example.com", stored.DocumentRoot);
        Assert.False(stored.HasCertificate);
    }

    /// <summary>The pool a creation writes carries the settings the account's customer chose.</summary>
    [Fact]
    public async Task The_pool_a_creation_writes_carries_the_settings_the_account_chose()
    {
        // The agent's pool write replaces what is on disk, so a creation that sent no settings would
        // drop what the customer chose for the account's other sites.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), world.Database))
        {
            seed.PhpSettings.Add(new PhpSetting(Guid.NewGuid(), account, "memory_limit", "128M"));
            await seed.SaveChangesAsync();
        }

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(Assert.Single(world.Agent.SettingOverrides));
        Assert.Equal("memory_limit", sent.Name);
        Assert.Equal("128M", sent.Value);
    }

    /// <summary>Creating a site journals the domain as the subject.</summary>
    [Fact]
    public async Task Creating_a_site_journals_the_domain_as_the_subject()
    {
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);

        await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(SiteAuditActions.SiteCreated, entry.Action);
        Assert.Equal("example.com", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A site beyond the plans allowance is refused before the agent is called at all.</summary>
    [Fact]
    public async Task A_site_beyond_the_plans_allowance_is_refused_before_the_agent_is_called_at_all()
    {
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account, maxSites: 1);
        world.DbContext.Sites.Add(SitesTestContext.PhpSite(account, "first.example.com"));
        await world.DbContext.SaveChangesAsync();

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteLimitReached", result.Error!.Code);

        // Spec §8: a site the plan refuses must never reach the host. An empty call list is the
        // assertion — a limit checked after provisioning would leave a vhost nobody asked for.
        Assert.Empty(world.Agent.Calls);
        Assert.Equal(1, await world.DbContext.Sites.CountAsync());
    }

    /// <summary>A php version that is not installed as an app is refused before the agent is asked to create anything.</summary>
    [Fact]
    public async Task A_php_version_that_is_not_installed_as_an_app_is_refused_before_the_agent_is_asked_to_create_anything()
    {
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account, installedPhp: ["8.2"]);

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AppsNotInstalled", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>The same version, installed as an app, is accepted: the control for the refusal above.</summary>
    [Fact]
    public async Task A_php_version_installed_as_an_app_is_accepted()
    {
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account, installedPhp: ["8.2", "8.3"]);

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("8.3", result.Value.PhpVersion);
    }

    /// <summary>With no site web server installed the creation is refused before anything else is read or asked.</summary>
    [Fact]
    public async Task With_no_web_server_installed_the_creation_is_refused_before_anything_is_asked()
    {
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account, webServer: StackWebServer.None);

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AppsStackNotReady", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(world.Agent.Calls);
        Assert.Empty(await world.DbContext.Sites.ToListAsync());
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>The creation is rendered for the web server that is installed, whichever it is.</summary>
    /// <param name="installed">The installed web server.</param>
    /// <param name="expected">The web server the agent must be told.</param>
    [Theory]
    [InlineData(StackWebServer.Nginx, SiteWebServer.Nginx)]
    [InlineData(StackWebServer.Apache, SiteWebServer.Apache)]
    public async Task The_creation_is_rendered_for_the_installed_web_server(StackWebServer installed, SiteWebServer expected)
    {
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account, webServer: installed);

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, Assert.Single(world.Agent.WebServers));
    }

    /// <summary>A static site is created without any runtime installed.</summary>
    [Fact]
    public async Task A_static_site_is_created_without_any_runtime_installed()
    {
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account, installedPhp: [], installedNode: []);

        var result = await world.Handler().HandleAsync(
            CreateSiteWorld.Command(account) with { BackendType = SiteBackendType.Static, PhpVersion = string.Empty },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SiteNodeApp.None, Assert.Single(world.Agent.NodeApps));
    }

    /// <summary>A node site is provisioned with its runtime facts and stores them.</summary>
    [Fact]
    public async Task A_node_site_is_provisioned_with_its_runtime_facts_and_stores_them()
    {
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);

        var result = await world.Handler().HandleAsync(NodeCommand(account), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SiteBackendType.Node, result.Value.BackendType);
        Assert.Equal(new SiteNodeApp("22", 3100, "npm start"), Assert.Single(world.Agent.NodeApps));
        var stored = await world.DbContext.Sites.SingleAsync();
        Assert.Equal("22", stored.NodeVersion);
        Assert.Equal(3100, stored.AppPort);
        Assert.Equal("npm start", stored.StartCommand);
    }

    /// <summary>A node major that is not installed as an app is refused before the agent is asked to create anything.</summary>
    [Fact]
    public async Task A_node_major_that_is_not_installed_is_refused_before_the_agent_is_asked_to_create_anything()
    {
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account, installedNode: ["20"]);

        var result = await world.Handler().HandleAsync(NodeCommand(account), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AppsNotInstalled", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>A port another site already listens on is refused, whichever account owns that site.</summary>
    [Fact]
    public async Task A_port_another_site_listens_on_is_refused_whichever_account_owns_it()
    {
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), world.Database))
        {
            seed.Sites.Add(new Site(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "other.example.com",
                [],
                SiteBackendType.Node,
                string.Empty,
                string.Empty,
                "22",
                3100,
                "node app.js",
                "/home/other/sites/other.example.com",
                CreateSiteWorld.Now));
            await seed.SaveChangesAsync();
        }

        var taken = await world.Handler().HandleAsync(NodeCommand(account), CancellationToken.None);
        var free = await world.Handler().HandleAsync(NodeCommand(account) with { AppPort = 3101, Domain = "free.example.com" }, CancellationToken.None);

        Assert.False(taken.IsSuccess);
        Assert.Equal("SiteAppPortTaken", taken.Error!.Code);
        Assert.Equal(ErrorType.Conflict, taken.Error.Type);
        Assert.True(free.IsSuccess);
    }

    /// <summary>A refused provisioning leaves no site row behind.</summary>
    [Fact]
    public async Task A_refused_provisioning_leaves_no_site_row_behind()
    {
        // The agent runs before the row precisely so this is the failure mode: nothing was written,
        // and the caller sees the agent's own typed error rather than a site that does not serve.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account, agentFailure: Error.Of("AgentValidationFailed", ErrorType.Validation));

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

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
        var world = new CreateSiteWorld(account);

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(Guid.NewGuid()), CancellationToken.None);

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
        var world = new CreateSiteWorld(account, maxSites: 2, asAdministrator: true);
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), world.Database))
        {
            seed.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "someone-else-1.example.com"));
            seed.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "someone-else-2.example.com"));
            seed.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "someone-else-3.example.com"));
            await seed.SaveChangesAsync();
        }

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>The plan limit counts only the owning accounts sites.</summary>
    [Fact]
    public async Task The_plan_limit_counts_only_the_owning_accounts_sites()
    {
        // The other direction: the account is genuinely at its limit, and reaching it must not
        // depend on who is asking.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account, maxSites: 1, asAdministrator: true);
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), world.Database))
        {
            seed.Sites.Add(SitesTestContext.PhpSite(account, "already.example.com"));
            await seed.SaveChangesAsync();
        }

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteLimitReached", result.Error!.Code);
    }

    /// <summary>The stored site keeps the aliases it was created with.</summary>
    [Fact]
    public async Task The_stored_site_keeps_the_aliases_it_was_created_with()
    {
        // Asserted on the ROW, not only on the agent argument. The row is what every later
        // re-render reads (SiteDescriptorMapper), so aliases dropped on the way into the database
        // would survive creation intact and then vanish from the vhost on the first enable, disable
        // or version change — long after anyone would connect the two.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);

        await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        var stored = await world.DbContext.Sites.SingleAsync();
        Assert.Equal(["www.example.com"], stored.Aliases);
    }

    /// <summary>The requests cancellation token reaches the agent.</summary>
    [Fact]
    public async Task The_requests_cancellation_token_reaches_the_agent()
    {
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);
        using var cancellation = new CancellationTokenSource();

        await world.Handler().HandleAsync(CreateSiteWorld.Command(account), cancellation.Token);

        Assert.Equal(cancellation.Token, Assert.Single(world.Agent.Tokens));
    }

    /// <summary>A refused creation is journalled as a failure.</summary>
    [Fact]
    public async Task A_refused_creation_is_journalled_as_a_failure()
    {
        // AuditEntry says failures are the half of the journal worth reading, and a refused
        // provisioning is exactly the event an operator later needs to explain a missing site.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account, agentFailure: Error.Of("AgentSystemFailure", ErrorType.Failure));

        await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal(SiteAuditActions.SiteCreated, entry.Action);
        Assert.Equal("example.com", entry.Subject);
    }

    /// <summary>A creation refused by the plan limit is journalled as a failure.</summary>
    [Fact]
    public async Task A_creation_refused_by_the_plan_limit_is_journalled_as_a_failure()
    {
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account, maxSites: 1);
        world.DbContext.Sites.Add(SitesTestContext.PhpSite(account, "first.example.com"));
        await world.DbContext.SaveChangesAsync();

        await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>The journal names the caller who made the change.</summary>
    [Fact]
    public async Task The_journal_names_the_caller_who_made_the_change()
    {
        // Both halves: an id nobody can read is not an answer to "who did what", and a blank name
        // is what every Sites entry carried before the audit journal read the caller's name.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);

        await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        var entry = Assert.Single(world.Audit.Entries);
        var caller = Assert.IsAssignableFrom<PrincipalCaller>(world.CurrentUser.Caller);
        Assert.Equal(caller.UserId, entry.ActorUserId);
        Assert.Equal(caller.Username, entry.ActorUsername);
        Assert.NotEqual(string.Empty, entry.ActorUsername);
    }

    /// <summary>Builds a creation of a Node site on Node 22, port 3100.</summary>
    /// <param name="accountId">The account the site is created for.</param>
    private static CreateSiteCommand NodeCommand(Guid accountId)
    {
        return new CreateSiteCommand(
            accountId,
            "app.example.com",
            SiteBackendType.Node,
            [],
            NodeVersion: "22",
            AppPort: 3100,
            StartCommand: "npm start");
    }
}
