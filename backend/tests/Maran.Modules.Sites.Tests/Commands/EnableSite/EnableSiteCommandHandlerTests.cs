using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Commands.EnableSite;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.Commands.EnableSite;

/// <summary>Behavioral contract of <see cref="EnableSiteCommandHandler"/>.</summary>
public sealed class EnableSiteCommandHandlerTests
{
    /// <summary>Enabling a site re renders its vhost and then stores the new status.</summary>
    [Fact]
    public async Task Enabling_a_site_re_renders_its_vhost_and_then_stores_the_new_status()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account);
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context, account, agent).HandleAsync(
            new EnableSiteCommand(siteId, "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SiteStatus.Enabled, result.Value.Status);
        Assert.Equal("enable:acme:example.com", Assert.Single(agent.Calls));
        Assert.Equal(SiteStatus.Enabled, (await context.Sites.SingleAsync()).Status);
    }

    /// <summary>The descriptor handed to the agent carries the stored sites own facts.</summary>
    [Fact]
    public async Task The_descriptor_handed_to_the_agent_carries_the_stored_sites_own_facts()
    {
        // A descriptor invented at the call site is the defect this guards: a literal
        // HasCertificate false would re-render a live site without its TLS block.
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "www.example.com");
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        await Handler(context, account, agent).HandleAsync(
            new EnableSiteCommand(siteId, "198.51.100.7", "tests"),
            CancellationToken.None);

        var descriptor = Assert.Single(agent.Descriptors);
        Assert.Equal(["www.example.com"], descriptor.Aliases);
        Assert.Equal(SiteBackendKind.Php, descriptor.Backend);
        Assert.Equal("8.3", descriptor.PhpVersion);
    }

    /// <summary>A refused re render leaves the stored status unchanged.</summary>
    [Fact]
    public async Task A_refused_re_render_leaves_the_stored_status_unchanged()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account);
        // The agent client's own code for a config the web server refused: AgentErrorTranslator is
        // the single wire-error boundary, so what a handler receives is one of ITS codes, never a
        // code this module invented (rules/csharp.md).
        var agent = new RecordingAgentSitesClient(Error.Of("AgentValidationFailed"));
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context, account, agent).HandleAsync(
            new EnableSiteCommand(siteId, "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentValidationFailed", result.Error!.Code);
        Assert.Equal(SiteStatus.Disabled, (await context.Sites.SingleAsync()).Status);
    }

    /// <summary>Enabling another tenants site answers not found rather than forbidden.</summary>
    [Fact]
    public async Task Enabling_another_tenants_site_answers_not_found_rather_than_forbidden()
    {
        // The IDOR test (rules/testing.md Definition of Done 3): 403 would confirm the id names a
        // real site, which is all it takes to enumerate other customers' domains.
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var theirSiteId = await SeedAsync(database, theirs);
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);

        var result = await Handler(context, mine, agent).HandleAsync(
            new EnableSiteCommand(theirSiteId, "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteNotFound", result.Error!.Code);
        Assert.Empty(agent.Calls);
    }

    /// <summary>Enabling a site journals the domain as the subject.</summary>
    [Fact]
    public async Task Enabling_a_site_journals_the_domain_as_the_subject()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account);
        var audit = new RecordingAuditWriter();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        await Handler(context, account, new RecordingAgentSitesClient(), audit).HandleAsync(
            new EnableSiteCommand(siteId, "198.51.100.7", "tests"),
            CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.SiteEnabled, entry.Action);
        Assert.Equal("example.com", entry.Subject);
    }

    /// <summary>The new status survives into a fresh context.</summary>
    [Fact]
    public async Task The_new_status_survives_into_a_fresh_context()
    {
        // Read back through a SECOND context on the same database. Reading through the handler own
        // context proves nothing about persistence: the change tracker returns the modified entity
        // whether or not SaveChangesAsync was ever called.
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account);
        await using (var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database))
        {
            await Handler(context, account, new RecordingAgentSitesClient()).HandleAsync(
                new EnableSiteCommand(siteId, "198.51.100.7", "tests"),
                CancellationToken.None);
        }

        await using var reader = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        Assert.Equal(SiteStatus.Enabled, (await reader.Sites.SingleAsync()).Status);
    }

    /// <summary>The requests cancellation token reaches the agent.</summary>
    [Fact]
    public async Task The_requests_cancellation_token_reaches_the_agent()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account);
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        using var cancellation = new CancellationTokenSource();

        await Handler(context, account, agent).HandleAsync(
            new EnableSiteCommand(siteId, "198.51.100.7", "tests"),
            cancellation.Token);

        Assert.Equal(cancellation.Token, Assert.Single(agent.Tokens));
    }

    /// <summary>A refused operation is journalled as a failure.</summary>
    [Fact]
    public async Task A_refused_operation_is_journalled_as_a_failure()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account);
        var audit = new RecordingAuditWriter();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        await Handler(context, account, new RecordingAgentSitesClient(Error.Of("AgentSystemFailure")), audit)
            .HandleAsync(new EnableSiteCommand(siteId, "198.51.100.7", "tests"), CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal("example.com", entry.Subject);
    }

    /// <summary>Enabling a site that is already enabled succeeds again and changes nothing.</summary>
    [Fact]
    public async Task Enabling_a_site_that_is_already_enabled_succeeds_again_and_changes_nothing()
    {
        // The idempotency half of the Definition of Done, and it is a contract rather than an
        // accident: every agent operation converges on repetition, which is what makes a retry
        // after a timeout safe. A handler that started refusing the second call — or that stopped
        // re-rendering and so let a hand-edited vhost survive — would break the retry the whole
        // chain is built on, and nothing else in this suite calls it twice.
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account);
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        var handler = Handler(context, account, agent);
        var command = new EnableSiteCommand(siteId, "198.51.100.7", "tests");

        var first = await handler.HandleAsync(command, CancellationToken.None);
        var second = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(SiteStatus.Enabled, second.Value.Status);
        Assert.Equal(["enable:acme:example.com", "enable:acme:example.com"], agent.Calls);
        Assert.Equal(SiteStatus.Enabled, (await context.Sites.SingleAsync()).Status);
    }

    /// <summary>Builds the handler under test.</summary>
    /// <param name="context">The context bound to the calling principal.</param>
    /// <param name="accountId">The account the caller owns.</param>
    /// <param name="agent">The agent double.</param>
    /// <param name="audit">The journal double.</param>
    private static EnableSiteCommandHandler Handler(
        SitesDbContext context,
        Guid accountId,
        RecordingAgentSitesClient agent,
        RecordingAuditWriter? audit = null)
    {
        var accounts = new StubAccountDirectory(
            new AccountSnapshot(accountId, "acme", MaxSites: 5, MaxPhpWorkersPerPool: 10));
        return new EnableSiteCommandHandler(
            context,
            accounts,
            agent,
            new SiteAuditJournal(audit ?? new RecordingAuditWriter(), FakeCurrentUser.Customer(accountId)));
    }

    /// <summary>Seeds one site in the state this operation acts from, and returns its id.</summary>
    /// <param name="database">The shared in-memory database.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="aliases">Additional hostnames answered by the same vhost.</param>
    private static async Task<Guid> SeedAsync(string database, Guid accountId, params string[] aliases)
    {
        await using var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var site = SitesTestContext.PhpSite(accountId, "example.com", "8.3", aliases);
        PutInStartingState(site);
        seed.Sites.Add(site);
        await seed.SaveChangesAsync();
        return site.Id;
    }

    /// <summary>Puts a freshly built site into the state this operation is meant to act from.</summary>
    /// <param name="site">The seeded site.</param>
    private static void PutInStartingState(Site site)
    {
        site.Disable();
    }
}
