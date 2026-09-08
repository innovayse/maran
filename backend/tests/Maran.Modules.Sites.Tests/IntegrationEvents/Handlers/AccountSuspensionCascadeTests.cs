using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.IntegrationEvents.Handlers;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Events;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.IntegrationEvents.Handlers;

/// <summary>
/// What the Sites module does when an account is suspended and when it is resumed. The pair is
/// tested in one file because the reversal is not the mirror of the pause, and each half only makes
/// sense against the other: everything is stubbed, and only what the customer had enabled comes back.
/// </summary>
public sealed class AccountSuspensionCascadeTests
{
    /// <summary>The system user name the events carry.</summary>
    private const string Username = "acme";

    /// <summary>Suspending an account puts every one of its sites onto the suspended vhost.</summary>
    /// <remarks>
    /// The defect this handler exists for: before it, a suspended customer's websites kept serving
    /// their own pages, and the only thing the suspension changed was the account's login shell.
    /// </remarks>
    [Fact]
    public async Task Suspending_an_account_puts_every_one_of_its_sites_onto_the_suspended_vhost()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, SitesTestContext.PhpSite(account, "b.example.com"),
            SitesTestContext.PhpSite(account, "a.example.com"));
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        await new AccountSuspendingHandler(context, agent).HandleAsync(
            new AccountSuspending(account, Username), CancellationToken.None);

        Assert.Equal(["disable:acme:a.example.com", "disable:acme:b.example.com"], agent.Calls);
    }

    /// <summary>Suspending leaves every site's stored status exactly as the customer set it.</summary>
    /// <remarks>
    /// The law of this handler. <c>Site.Status</c> is the customer's own choice; if a suspension
    /// overwrote it, resuming would have nothing left to tell "the customer disabled this" from "the
    /// suspension disabled this" and every site would come back enabled.
    /// </remarks>
    [Fact]
    public async Task Suspending_leaves_every_sites_stored_status_exactly_as_the_customer_set_it()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var disabled = SitesTestContext.PhpSite(account, "off.example.com");
        disabled.Disable();
        await SeedAsync(database, SitesTestContext.PhpSite(account, "on.example.com"), disabled);
        await using (var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            await new AccountSuspendingHandler(context, new RecordingAgentSitesClient()).HandleAsync(
                new AccountSuspending(account, Username), CancellationToken.None);
        }

        await using var reader = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var stored = await reader.Sites.OrderBy(site => site.Domain).ToListAsync();
        Assert.Equal(SiteStatus.Disabled, stored[0].Status);
        Assert.Equal(SiteStatus.Enabled, stored[1].Status);
    }

    /// <summary>An agent that refuses one vhost aborts the suspension rather than skipping it.</summary>
    /// <remarks>
    /// Throwing is the only way a subscriber can stop the account being recorded as suspended, and a
    /// site still serving under an account the panel calls suspended is exactly what the cascade is
    /// for. Returning quietly here would produce that state on every partial failure.
    /// </remarks>
    [Fact]
    public async Task An_agent_that_refuses_one_vhost_aborts_the_suspension_rather_than_skipping_it()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, SitesTestContext.PhpSite(account, "a.example.com"));
        var agent = new RecordingAgentSitesClient(Error.Of("AgentValidationFailed", ErrorType.Validation));
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await new AccountSuspendingHandler(context, agent).HandleAsync(
                new AccountSuspending(account, Username), CancellationToken.None);
        });
    }

    /// <summary>Another account's sites are not touched by a suspension.</summary>
    [Fact]
    public async Task Another_accounts_sites_are_not_touched_by_a_suspension()
    {
        var suspended = Guid.NewGuid();
        var neighbour = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database,
            SitesTestContext.PhpSite(suspended, "mine.example.com"),
            SitesTestContext.PhpSite(neighbour, "theirs.example.com"));
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        await new AccountSuspendingHandler(context, agent).HandleAsync(
            new AccountSuspending(suspended, Username), CancellationToken.None);

        Assert.Equal(["disable:acme:mine.example.com"], agent.Calls);
    }

    /// <summary>The descriptor handed to the agent carries the stored site's own facts.</summary>
    /// <remarks>
    /// Not cosmetic. The agent decides whether a vhost is stubbed by comparing what is on disk with
    /// what this descriptor renders, so a descriptor invented at the call site would produce a file
    /// that is suspended and does not compare equal — which the attestation reports as NOT stubbed,
    /// refusing a suspension that had in fact worked.
    /// </remarks>
    [Fact]
    public async Task The_descriptor_handed_to_the_agent_carries_the_stored_sites_own_facts()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, SitesTestContext.PhpSite(account, "example.com", "8.3", "www.example.com"));
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        await new AccountSuspendingHandler(context, agent).HandleAsync(
            new AccountSuspending(account, Username), CancellationToken.None);

        var descriptor = Assert.Single(agent.Descriptors);
        Assert.Equal(["www.example.com"], descriptor.Aliases);
        Assert.Equal("8.3", descriptor.PhpVersion);
    }

    /// <summary>Resuming restores the enabled sites and leaves a customer disabled one stubbed.</summary>
    /// <remarks>
    /// The reversal law, and the assertion that fails if anyone "simplifies" the resume into
    /// enable-everything. Its agent-side twin is the polygon test
    /// <c>resuming_restores_the_site_that_was_resumed_and_leaves_the_other_one_stubbed</c>; this is
    /// the half only the panel can hold up, because only the panel has <c>Site.Status</c>.
    /// </remarks>
    [Fact]
    public async Task Resuming_restores_the_enabled_sites_and_leaves_a_customer_disabled_one_stubbed()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var disabled = SitesTestContext.PhpSite(account, "off.example.com");
        disabled.Disable();
        await SeedAsync(database, SitesTestContext.PhpSite(account, "on.example.com"), disabled);
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        await new AccountResumingHandler(context, agent).HandleAsync(
            new AccountResuming(account, Username), CancellationToken.None);

        Assert.Equal(["enable:acme:on.example.com"], agent.Calls);
    }

    /// <summary>An agent that refuses to restore a vhost aborts the resumption.</summary>
    [Fact]
    public async Task An_agent_that_refuses_to_restore_a_vhost_aborts_the_resumption()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, SitesTestContext.PhpSite(account, "a.example.com"));
        var agent = new RecordingAgentSitesClient(Error.Of("AgentSystemFailure", ErrorType.Failure));
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await new AccountResumingHandler(context, agent).HandleAsync(
                new AccountResuming(account, Username), CancellationToken.None);
        });
    }

    /// <summary>Both handlers act on an account whose sites a request could not see.</summary>
    /// <remarks>
    /// The tenant query filter governs what a REQUEST may see; a suspension is the panel acting on
    /// the account the rows belong to, authorised before the event was invoked. This is the control
    /// that proves the filter bypass is real: the context is bound to a DIFFERENT customer, so a
    /// handler that queried through the filter would find nothing and quietly do nothing at all —
    /// leaving every site serving while the account was recorded as suspended.
    /// </remarks>
    [Fact]
    public async Task Both_handlers_act_on_an_account_whose_sites_a_request_could_not_see()
    {
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, SitesTestContext.PhpSite(owner, "a.example.com"));
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(stranger), database);

        await new AccountSuspendingHandler(context, agent).HandleAsync(
            new AccountSuspending(owner, Username), CancellationToken.None);
        await new AccountResumingHandler(context, agent).HandleAsync(
            new AccountResuming(owner, Username), CancellationToken.None);

        Assert.Equal(["disable:acme:a.example.com", "enable:acme:a.example.com"], agent.Calls);
    }

    /// <summary>An account with no sites asks the agent for nothing.</summary>
    /// <remarks>
    /// The inverse control on a refusing pair: both handlers must ACCEPT the case where there is
    /// nothing to do, rather than throwing, or every suspension of a site-less account would fail.
    /// </remarks>
    [Fact]
    public async Task An_account_with_no_sites_asks_the_agent_for_nothing()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        await new AccountSuspendingHandler(context, agent).HandleAsync(
            new AccountSuspending(account, Username), CancellationToken.None);
        await new AccountResumingHandler(context, agent).HandleAsync(
            new AccountResuming(account, Username), CancellationToken.None);

        Assert.Empty(agent.Calls);
    }

    /// <summary>Seeds the given sites into a shared in-memory database.</summary>
    /// <param name="database">The in-memory database name to seed.</param>
    /// <param name="sites">The rows to write.</param>
    /// <returns>Resolves once the rows are stored.</returns>
    private static async Task SeedAsync(string database, params Site[] sites)
    {
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        context.Sites.AddRange(sites);
        await context.SaveChangesAsync();
    }
}
