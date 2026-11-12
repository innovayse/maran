using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.IntegrationEvents.Events;
using Maran.Modules.Sites.IntegrationEvents.Handlers;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.Sdk.Modules;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.IntegrationEvents.Handlers;

/// <summary>Behavioral contract of <see cref="PhpSiteVhostsRerenderRequestedHandler"/>.</summary>
public sealed class PhpSiteVhostsRerenderRequestedHandlerTests
{
    /// <summary>A plan whose upload bound is 200 MiB, the figure the tests expect on the wire.</summary>
    private static readonly PhpLimitsSnapshot Bound200 = new(256, 200, 120, 3000);

    /// <summary>Every enabled PHP site is rendered again through the version change with its stored version, carrying the bound.</summary>
    [Fact]
    public async Task Every_enabled_php_site_is_rendered_again_with_its_stored_version_and_the_new_bound()
    {
        var accountId = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        context.Sites.Add(SitesTestContext.PhpSite(accountId, "a.example", "8.3"));
        context.Sites.Add(SitesTestContext.PhpSite(accountId, "b.example", "8.4"));
        await context.SaveChangesAsync();
        var sites = new RecordingAgentSitesClient();

        await Handler(context, sites, Active(accountId)).HandleAsync(new PhpSiteVhostsRerenderRequested(accountId), CancellationToken.None);

        Assert.Equal(["change-php:acme:a.example:8.3:False", "change-php:acme:b.example:8.4:False"], sites.Calls);
        Assert.All(sites.Descriptors, descriptor => { Assert.Equal(200u, descriptor.Options.ClientMaxBodySizeMib); });
        Assert.All(sites.MaxChildren, children => { Assert.Equal(10u, children); });
    }

    /// <summary>The enable rpc, the deliberate un-suspend, is never used (inverse control of the suspension test).</summary>
    [Fact]
    public async Task The_enable_rpc_is_never_used()
    {
        var accountId = Guid.NewGuid();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), Guid.NewGuid().ToString());
        context.Sites.Add(SitesTestContext.PhpSite(accountId, "a.example"));
        await context.SaveChangesAsync();
        var sites = new RecordingAgentSitesClient();

        await Handler(context, sites, Active(accountId)).HandleAsync(new PhpSiteVhostsRerenderRequested(accountId), CancellationToken.None);

        Assert.DoesNotContain(sites.Calls, call => { return call.StartsWith("enable:", StringComparison.Ordinal); });
    }

    /// <summary>A suspended account's sites stay suspended: the agent is not called at all.</summary>
    [Fact]
    public async Task A_suspended_accounts_sites_are_not_touched()
    {
        var accountId = Guid.NewGuid();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), Guid.NewGuid().ToString());
        context.Sites.Add(SitesTestContext.PhpSite(accountId, "a.example"));
        await context.SaveChangesAsync();
        var sites = new RecordingAgentSitesClient();

        await Handler(context, sites, Active(accountId) with { State = AccountState.Suspended })
            .HandleAsync(new PhpSiteVhostsRerenderRequested(accountId), CancellationToken.None);

        Assert.Empty(sites.Calls);
    }

    /// <summary>A disabled site and a site that is not PHP are not rendered (inverse controls).</summary>
    [Fact]
    public async Task A_disabled_site_and_a_site_that_is_not_php_are_not_rendered()
    {
        var accountId = Guid.NewGuid();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), Guid.NewGuid().ToString());
        var disabled = SitesTestContext.PhpSite(accountId, "off.example");
        disabled.Disable();
        context.Sites.Add(disabled);
        context.Sites.Add(SitesTestContext.PhpSite(accountId, "on.example"));
        context.Sites.Add(StaticSite(accountId, "static.example"));
        await context.SaveChangesAsync();
        var sites = new RecordingAgentSitesClient();

        await Handler(context, sites, Active(accountId)).HandleAsync(new PhpSiteVhostsRerenderRequested(accountId), CancellationToken.None);

        Assert.Equal(["change-php:acme:on.example:8.3:False"], sites.Calls);
    }

    /// <summary>A site disabled by a concurrent request while the pass is running is not rendered afterwards.</summary>
    [Fact]
    public async Task A_site_disabled_while_the_pass_runs_is_not_rendered_afterwards()
    {
        var accountId = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        context.Sites.Add(SitesTestContext.PhpSite(accountId, "a.example"));
        context.Sites.Add(SitesTestContext.PhpSite(accountId, "b.example"));
        await context.SaveChangesAsync();
        var sites = new RecordingAgentSitesClient
        {
            // The barrier: while the first site is being rendered, another request commits the disable of
            // the second one, exactly between the pass's listing and its call for that site.
            BeforeChangePhpVersion = async domain =>
            {
                if (domain != "a.example")
                {
                    return;
                }

                await using var other = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
                (await other.Sites.SingleAsync(site => site.Domain == "b.example")).Disable();
                await other.SaveChangesAsync();
            },
        };

        await Handler(context, sites, Active(accountId)).HandleAsync(new PhpSiteVhostsRerenderRequested(accountId), CancellationToken.None);

        Assert.Equal(["change-php:acme:a.example:8.3:False"], sites.Calls);
    }

    /// <summary>A site deleted by a concurrent request while the pass is running is not rendered afterwards.</summary>
    [Fact]
    public async Task A_site_deleted_while_the_pass_runs_is_not_rendered_afterwards()
    {
        var accountId = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        context.Sites.Add(SitesTestContext.PhpSite(accountId, "a.example"));
        context.Sites.Add(SitesTestContext.PhpSite(accountId, "b.example"));
        await context.SaveChangesAsync();
        var sites = new RecordingAgentSitesClient
        {
            BeforeChangePhpVersion = async domain =>
            {
                if (domain != "a.example")
                {
                    return;
                }

                await using var other = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
                other.Sites.Remove(await other.Sites.SingleAsync(site => site.Domain == "b.example"));
                await other.SaveChangesAsync();
            },
        };

        await Handler(context, sites, Active(accountId)).HandleAsync(new PhpSiteVhostsRerenderRequested(accountId), CancellationToken.None);

        Assert.Equal(["change-php:acme:a.example:8.3:False"], sites.Calls);
    }

    /// <summary>A refusal and an exception are logged per site and the remaining sites are still rendered.</summary>
    [Fact]
    public async Task A_refusal_and_an_exception_are_logged_per_site_and_the_others_are_still_rendered()
    {
        var accountId = Guid.NewGuid();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), Guid.NewGuid().ToString());
        context.Sites.Add(SitesTestContext.PhpSite(accountId, "a.example"));
        context.Sites.Add(SitesTestContext.PhpSite(accountId, "b.example"));
        context.Sites.Add(SitesTestContext.PhpSite(accountId, "c.example"));
        await context.SaveChangesAsync();
        var sites = new RecordingAgentSitesClient();
        sites.ChangePhpFailingDomains.Add("a.example");
        sites.ChangePhpThrowingDomains.Add("b.example");
        var log = new RecordingLogger<PhpSiteVhostsRerenderRequestedHandler>();

        await Handler(context, sites, log, Active(accountId)).HandleAsync(new PhpSiteVhostsRerenderRequested(accountId), CancellationToken.None);

        Assert.Equal(3, sites.Calls.Count);
        Assert.Equal(2, log.Entries.Count);
        Assert.Contains(log.Entries, entry => { return entry.Message.Contains("a.example", StringComparison.Ordinal); });
        Assert.Contains(log.Entries, entry => { return entry.Message.Contains("b.example", StringComparison.Ordinal); });
    }

    /// <summary>Cancelling the message stops the pass instead of being logged as a site's failure.</summary>
    [Fact]
    public async Task Cancelling_the_message_stops_the_pass()
    {
        var accountId = Guid.NewGuid();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), Guid.NewGuid().ToString());
        context.Sites.Add(SitesTestContext.PhpSite(accountId, "a.example"));
        await context.SaveChangesAsync();
        using var source = new CancellationTokenSource();
        var sites = new RecordingAgentSitesClient
        {
            BeforeChangePhpVersion = _ =>
            {
                source.Cancel();
                source.Token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        {
            return Handler(context, sites, Active(accountId)).HandleAsync(new PhpSiteVhostsRerenderRequested(accountId), source.Token);
        });
    }

    /// <summary>An account the directory cannot read, and a host with no web server, render nothing.</summary>
    [Fact]
    public async Task An_unreadable_account_and_a_host_with_no_web_server_render_nothing()
    {
        var accountId = Guid.NewGuid();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), Guid.NewGuid().ToString());
        context.Sites.Add(SitesTestContext.PhpSite(accountId, "a.example"));
        await context.SaveChangesAsync();
        var sites = new RecordingAgentSitesClient();

        await Handler(context, sites, Active(Guid.NewGuid())).HandleAsync(new PhpSiteVhostsRerenderRequested(accountId), CancellationToken.None);
        await Handler(context, sites, new RecordingLogger<PhpSiteVhostsRerenderRequestedHandler>(), SiteStacks.NotReady(), Active(accountId))
            .HandleAsync(new PhpSiteVhostsRerenderRequested(accountId), CancellationToken.None);

        Assert.Empty(sites.Calls);
    }

    /// <summary>An active account on the 200 MiB plan.</summary>
    /// <param name="accountId">The account's id.</param>
    private static AccountSnapshot Active(Guid accountId)
    {
        return Snapshots.Account(accountId, "acme") with { PhpLimits = Bound200 };
    }

    /// <summary>A static site row, which the plan's upload bound does not govern.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The site's primary domain.</param>
    private static Site StaticSite(Guid accountId, string domain)
    {
        return new Site(
            Guid.NewGuid(), accountId, domain, [], SiteBackendType.Static,
            string.Empty, string.Empty, string.Empty, 0, string.Empty, $"/home/acme/sites/{domain}",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>Builds the handler over the doubles with a ready stack.</summary>
    /// <param name="context">The context the sites are read from.</param>
    /// <param name="sites">The agent sites double.</param>
    /// <param name="account">The account the directory knows.</param>
    private static PhpSiteVhostsRerenderRequestedHandler Handler(
        SitesDbContext context,
        RecordingAgentSitesClient sites,
        AccountSnapshot account)
    {
        return Handler(context, sites, new RecordingLogger<PhpSiteVhostsRerenderRequestedHandler>(), account);
    }

    /// <summary>Builds the handler over the doubles with a ready stack and a logger the test can read.</summary>
    /// <param name="context">The context the sites are read from.</param>
    /// <param name="sites">The agent sites double.</param>
    /// <param name="log">Where the handler reports a failure.</param>
    /// <param name="account">The account the directory knows.</param>
    private static PhpSiteVhostsRerenderRequestedHandler Handler(
        SitesDbContext context,
        RecordingAgentSitesClient sites,
        RecordingLogger<PhpSiteVhostsRerenderRequestedHandler> log,
        AccountSnapshot account)
    {
        return Handler(context, sites, log, SiteStacks.Ready(), account);
    }

    /// <summary>Builds the handler over the doubles.</summary>
    /// <param name="context">The context the sites are read from.</param>
    /// <param name="sites">The agent sites double.</param>
    /// <param name="log">Where the handler reports a failure.</param>
    /// <param name="stack">The installed stack.</param>
    /// <param name="account">The account the directory knows.</param>
    /// <param name="locks">The per-site lock; defaults to a private real one, shared when a test passes it.</param>
    private static PhpSiteVhostsRerenderRequestedHandler Handler(
        SitesDbContext context,
        RecordingAgentSitesClient sites,
        RecordingLogger<PhpSiteVhostsRerenderRequestedHandler> log,
        SiteStackGate stack,
        AccountSnapshot account,
        ISiteOperationLock? locks = null)
    {
        return new PhpSiteVhostsRerenderRequestedHandler(
            context,
            sites,
            stack,
            new SitePlanLimits(new StubAccountDirectory(account)),
            new PhpAccountPoolSettings(context),
            log,
            locks ?? new SiteOperationLock());
    }
}
