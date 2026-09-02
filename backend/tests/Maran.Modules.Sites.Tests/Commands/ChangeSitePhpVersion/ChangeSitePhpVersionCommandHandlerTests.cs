using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Commands.ChangeSitePhpVersion;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.Commands.ChangeSitePhpVersion;

/// <summary>Behavioral contract of <see cref="ChangeSitePhpVersionCommandHandler"/>.</summary>
public sealed class ChangeSitePhpVersionCommandHandlerTests
{
    /// <summary>Rebinding a site to an installed version re-renders the pool and then stores it.</summary>
    [Fact]
    public async Task Rebinding_a_site_to_an_installed_version_re_renders_the_pool_and_then_stores_it()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context, account, agent).HandleAsync(
            new ChangeSitePhpVersionCommand(siteId, "8.4", "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("8.4", result.Value.PhpVersion);
        // The trailing True says the pool of the version being LEFT may go: this is the account's
        // only site on 8.3, so nothing else is served by that pool.
        Assert.Equal("change-php:acme:example.com:8.4:True", Assert.Single(agent.Calls));
        Assert.Equal("8.4", (await context.Sites.SingleAsync()).PhpVersion);
    }

    /// <summary>The pool is rendered with the plans worker budget.</summary>
    [Fact]
    public async Task The_pool_is_rendered_with_the_plans_worker_budget()
    {
        // pm.max_children is where a plan's CPU ceiling is actually enforced (spec §8, §11). A
        // fabricated default here would silently give every customer the same ceiling.
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        await Handler(context, account, agent, maxPhpWorkersPerPool: 37).HandleAsync(
            new ChangeSitePhpVersionCommand(siteId, "8.4", "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.Equal(37u, Assert.Single(agent.MaxChildren));
    }

    /// <summary>The descriptor handed to the agent carries the stored sites own facts.</summary>
    [Fact]
    public async Task The_descriptor_handed_to_the_agent_carries_the_stored_sites_own_facts()
    {
        // The failure this guards against is a call site inventing the descriptor — a literal
        // HasCertificate: false re-renders a live site's vhost without its TLS block and drops it
        // back to plain HTTP on an edit that had nothing to do with TLS.
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com", "www.example.com", "cdn.example.com");
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        await Handler(context, account, agent).HandleAsync(
            new ChangeSitePhpVersionCommand(siteId, "8.4", "198.51.100.7", "tests"),
            CancellationToken.None);

        var descriptor = Assert.Single(agent.Descriptors);
        Assert.Equal(["www.example.com", "cdn.example.com"], descriptor.Aliases);
        Assert.Equal(SiteBackendKind.Php, descriptor.Backend);
        Assert.Equal("8.3", descriptor.PhpVersion);
    }

    /// <summary>A version the host does not have is refused without touching the site.</summary>
    [Fact]
    public async Task A_version_the_host_does_not_have_is_refused_without_touching_the_site()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context, account, agent, installedPhp: ["8.3"]).HandleAsync(
            new ChangeSitePhpVersionCommand(siteId, "8.4", "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PhpVersionNotInstalled", result.Error!.Code);
        Assert.Empty(agent.Calls);
        Assert.Equal("8.3", (await context.Sites.SingleAsync()).PhpVersion);
    }

    /// <summary>A refused re render leaves the stored version unchanged.</summary>
    [Fact]
    public async Task A_refused_re_render_leaves_the_stored_version_unchanged()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        // The agent client's own code for a config the web server refused: AgentErrorTranslator is
        // the single wire-error boundary, so what a handler receives is one of ITS codes, never a
        // code this module invented (rules/csharp.md).
        var agent = new RecordingAgentSitesClient(Error.Of("AgentValidationFailed"));
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context, account, agent).HandleAsync(
            new ChangeSitePhpVersionCommand(siteId, "8.4", "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentValidationFailed", result.Error!.Code);
        Assert.Equal("8.3", (await context.Sites.SingleAsync()).PhpVersion);
    }

    /// <summary>Rebinding another tenants site answers not found rather than forbidden.</summary>
    [Fact]
    public async Task Rebinding_another_tenants_site_answers_not_found_rather_than_forbidden()
    {
        // The IDOR test (rules/testing.md Definition of Done 3). 403 would confirm the id names a
        // real site, which is all it takes to enumerate other customers' domains.
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var theirSiteId = await SeedAsync(database, theirs, "theirs.example.com");
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);

        var result = await Handler(context, mine, agent).HandleAsync(
            new ChangeSitePhpVersionCommand(theirSiteId, "8.4", "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteNotFound", result.Error!.Code);
        Assert.Empty(agent.Calls);
    }

    /// <summary>Rebinding a site journals the domain as the subject.</summary>
    [Fact]
    public async Task Rebinding_a_site_journals_the_domain_as_the_subject()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        var audit = new RecordingAuditWriter();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        await Handler(context, account, new RecordingAgentSitesClient(), audit: audit).HandleAsync(
            new ChangeSitePhpVersionCommand(siteId, "8.4", "198.51.100.7", "tests"),
            CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.SitePhpVersionChanged, entry.Action);
        Assert.Equal("example.com", entry.Subject);
    }

    /// <summary>A site whose backend is not php cannot be rebound.</summary>
    [Theory]
    [InlineData(SiteBackendType.Static)]
    [InlineData(SiteBackendType.ReverseProxy)]
    public async Task A_site_whose_backend_is_not_php_cannot_be_rebound(SiteBackendType backendType)
    {
        // Without the guard the agent is asked to render an FPM pool for a site whose descriptor
        // says Static, and the row ends up claiming BackendType=Static with a PhpVersion set — a
        // state no renderer can make sense of and nothing else in the module would ever produce.
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedNonPhpAsync(database, account, backendType);
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context, account, agent).HandleAsync(
            new ChangeSitePhpVersionCommand(siteId, "8.4", "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteBackendNotPhp", result.Error!.Code);
        Assert.Empty(agent.Calls);

        var stored = await context.Sites.SingleAsync();
        Assert.Equal(backendType, stored.BackendType);
        Assert.Equal(string.Empty, stored.PhpVersion);
    }

    /// <summary>A php site is still rebindable.</summary>
    [Fact]
    public async Task A_php_site_is_still_rebindable()
    {
        // The other direction of the guard, so it cannot be satisfied by refusing everything.
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context, account, new RecordingAgentSitesClient()).HandleAsync(
            new ChangeSitePhpVersionCommand(siteId, "8.4", "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>The new version survives into a fresh context.</summary>
    [Fact]
    public async Task The_new_version_survives_into_a_fresh_context()
    {
        // Read back through a SECOND context on the same database. Reading through the handler own
        // context proves nothing about persistence: the change tracker returns the modified entity
        // whether or not SaveChangesAsync was ever called.
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        await using (var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database))
        {
            await Handler(context, account, new RecordingAgentSitesClient()).HandleAsync(
                new ChangeSitePhpVersionCommand(siteId, "8.4", "198.51.100.7", "tests"),
                CancellationToken.None);
        }

        await using var reader = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        Assert.Equal("8.4", (await reader.Sites.SingleAsync()).PhpVersion);
    }

    /// <summary>The requests cancellation token reaches the agent.</summary>
    [Fact]
    public async Task The_requests_cancellation_token_reaches_the_agent()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        using var cancellation = new CancellationTokenSource();

        await Handler(context, account, agent).HandleAsync(
            new ChangeSitePhpVersionCommand(siteId, "8.4", "198.51.100.7", "tests"),
            cancellation.Token);

        Assert.Equal(cancellation.Token, Assert.Single(agent.Tokens));
    }

    /// <summary>A rebind for another tenants site is journalled as a failure naming what was probed for.</summary>
    [Fact]
    public async Task A_rebind_for_another_tenants_site_is_journalled_as_a_failure_naming_what_was_probed_for()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var theirSiteId = await SeedAsync(database, theirs, "theirs.example.com");
        var audit = new RecordingAuditWriter();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);

        await Handler(context, mine, new RecordingAgentSitesClient(), audit: audit).HandleAsync(
            new ChangeSitePhpVersionCommand(theirSiteId, "8.4", "198.51.100.7", "tests"),
            CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal(theirSiteId.ToString(), entry.Subject);
    }

    /// <summary>Seeds one non php site and returns its id.</summary>
    /// <param name="database">The shared in-memory database.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="backendType">The backend the seeded site runs.</param>
    private static async Task<Guid> SeedNonPhpAsync(string database, Guid accountId, SiteBackendType backendType)
    {
        await using var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var site = new Site(
            Guid.NewGuid(),
            accountId,
            "example.com",
            [],
            backendType,
            string.Empty,
            backendType == SiteBackendType.ReverseProxy ? "127.0.0.1:8080" : string.Empty,
            "/home/acct/sites/example.com",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        seed.Sites.Add(site);
        await seed.SaveChangesAsync();
        return site.Id;
    }

    /// <summary>Builds the handler under test.</summary>
    /// <param name="context">The context bound to the calling principal.</param>
    /// <param name="accountId">The account the caller owns.</param>
    /// <param name="agent">The agent double.</param>
    /// <param name="installedPhp">Versions the host reports as installed.</param>
    /// <param name="maxPhpWorkersPerPool">The plan's per-pool worker budget.</param>
    /// <param name="audit">The journal double.</param>
    private static ChangeSitePhpVersionCommandHandler Handler(
        Maran.Modules.Sites.Persistence.SitesDbContext context,
        Guid accountId,
        RecordingAgentSitesClient agent,
        string[]? installedPhp = null,
        int maxPhpWorkersPerPool = 10,
        RecordingAuditWriter? audit = null)
    {
        var accounts = new StubAccountDirectory(new AccountSnapshot(
            accountId,
            "acme",
            MaxSites: 5,
            MaxDatabases: 2,
            MaxSftpUsers: 3,
            MaxPhpWorkersPerPool: maxPhpWorkersPerPool));
        return new ChangeSitePhpVersionCommandHandler(
            context,
            accounts,
            agent,
            new RecordingAgentPhpClient(installedPhp ?? ["8.3", "8.4"]),
            new SiteAuditJournal(audit ?? new RecordingAuditWriter(), FakeCurrentUser.Customer(accountId)));
    }

    /// <summary>Seeds one site and returns its id.</summary>
    /// <param name="database">The shared in-memory database.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="aliases">Additional hostnames answered by the same vhost.</param>
    private static async Task<Guid> SeedAsync(string database, Guid accountId, string domain, params string[] aliases)
    {
        await using var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var site = SitesTestContext.PhpSite(accountId, domain, "8.3", aliases);
        seed.Sites.Add(site);
        await seed.SaveChangesAsync();
        return site.Id;
    }

    /// <summary>The old pool stays when the account has another site on the version being left.</summary>
    /// <remarks>
    /// The case that has to be got right: a pool is shared per account × version, so removing 8.3's
    /// pool because THIS site moved to 8.4 would take the account's other 8.3 site off the air —
    /// a site nobody touched,because of a change made to a different one.
    /// </remarks>
    [Fact]
    public async Task The_old_pool_stays_when_the_account_has_another_site_on_the_version_being_left()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        await using (var second = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            second.Sites.Add(SitesTestContext.PhpSite(account, "second.example.com"));
            await second.SaveChangesAsync();
        }

        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context, account, agent).HandleAsync(
            new ChangeSitePhpVersionCommand(siteId, "8.4", "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("change-php:acme:example.com:8.4:False", Assert.Single(agent.Calls));
    }
}
