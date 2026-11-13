using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.IntegrationEvents.Handlers;

/// <summary>
/// Replaces every vhost of an account that is about to be suspended with the suspended one
/// (<see cref="AccountSuspending"/>), so a suspended customer's websites stop serving their content.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Suspending an account locked its Linux login and nothing else. The panel
/// told billing the account was suspended while every one of its websites kept answering requests
/// exactly as before — the account's shell was the only thing that changed. This handler is the
/// site half of making that sentence true, and it is the first half to be built because it is the
/// one a customer and a visitor can both see.
/// </para>
/// <para>
/// <b>It writes no rows, and that is the law rather than an omission.</b> <c>Site.Status</c> is the
/// CUSTOMER's own choice about a site, and a suspension is an administrator's decision about an
/// account; if suspension wrote <c>Disabled</c> onto those rows, resuming would have nothing left to
/// tell "the customer disabled this" from "the suspension disabled this", and every site would come
/// back enabled — silently undoing a decision the customer made. So the stub is applied to the HOST
/// and the panel's record of what the customer wanted is left untouched, which is exactly what makes
/// <see cref="AccountResumingHandler"/> able to be selective.
/// </para>
/// <para>
/// <b>Disabled sites are stubbed too, deliberately.</b> The query is not filtered by status. A site
/// the customer had already disabled is already serving the suspended vhost, so the call is a no-op
/// the agent answers by comparing content — but asking for it costs nothing and means this handler
/// never has to be right about what state the host is in. The attestation afterwards checks every
/// vhost the HOST holds, not every row the panel remembers, and those two sets agreeing is not
/// something this handler is allowed to assume.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here propagates to the Accounts handler,
/// which abandons the suspension with the account left active — the recoverable direction. The
/// alternative is an account the panel and billing both believe is stopped, whose sites are still
/// serving; that is the precise state this handler was written to end.
/// </para>
/// </remarks>
public sealed class AccountSuspendingHandler
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The agent, which owns the vhost include directory.</summary>
    private readonly IAgentSitesClient _agent;

    /// <summary>Names the installed site web server, which every vhost this handler touches belongs to.</summary>
    private readonly SiteStackGate _stack;

    /// <summary>Reads the plan bounds the vhost's request body limit follows.</summary>
    private readonly SitePlanLimits _planLimits;

    /// <summary>Serialises the operations that change one site's host state.</summary>
    private readonly ISiteOperationLock _locks;

    /// <summary>The panel's clock, which bounds how long the cascade waits for busy sites.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="agent">The agent client that re-renders each vhost.</param>
    /// <param name="stack">Names the installed site web server the vhosts belong to.</param>
    /// <param name="planLimits">Reads the plan bounds the vhost's request body limit follows.</param>
    /// <param name="locks">Serialises the operations that change one site's host state.</param>
    /// <param name="clock">The panel's clock, which bounds how long the cascade waits for busy sites.</param>
    public AccountSuspendingHandler(SitesDbContext dbContext, IAgentSitesClient agent, SiteStackGate stack, SitePlanLimits planLimits,
        ISiteOperationLock locks, IClock clock)
    {
        _clock = clock;
        _locks = locks;
        _planLimits = planLimits;
        _dbContext = dbContext;
        _agent = agent;
        _stack = stack;
    }

    /// <summary>Puts every site the account owns onto the suspended vhost.</summary>
    /// <remarks>
    /// The tenant query filter is deliberately bypassed, for the reason the deletion cascade's
    /// handler sets out: the filter governs what a REQUEST may see, and this is the suspension of the
    /// account the rows belong to, authorised before the event was invoked. A filtered query here
    /// would answer correctly while suspension stays an administrator's operation and would leave a
    /// customer's sites serving, silently, the day it does not.
    /// </remarks>
    /// <param name="message">The account about to be suspended.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="InvalidOperationException">
    /// The agent refused to stub a vhost. Thrown rather than returned, because a subscriber's only
    /// way to abort the suspension is to fail — and an account marked suspended with a site still
    /// serving its own pages is the outcome this cascade exists to prevent. The agent's own text is
    /// carried in the exception, which the Accounts handler logs and never answers a customer with
    /// (rules/security.md item 8).
    /// </exception>
    public async Task HandleAsync(AccountSuspending message, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // the account is being suspended, so its rows must be found whoever asked for the suspension
        var owned = await _dbContext.Sites
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(site => site.AccountId == message.AccountId)
            .OrderBy(site => site.Domain)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030

        if (owned.Count == 0)
        {
            return;
        }

        var webServer = await _stack.RequireWebServerAsync(cancellationToken);
        var limitsRead = await _planLimits.ReadAsync(message.AccountId, cancellationToken);
        if (!limitsRead.IsSuccess)
        {
            // Failing is a subscriber's only way to abort the lifecycle step, and a vhost cannot be
            // rendered under a plan nobody can read.
            throw new InvalidOperationException(
                $"the plan of account {message.AccountId} could not be read: {limitsRead.Error?.Code}");
        }

        var limits = limitsRead.Value;
        // Every site is tried before this fails, so one refusal does not leave the others serving. The
        // account stays active, which is the recoverable direction, and repeating the suspension is the
        // retry: the stub is idempotent, so the sites already done cost the agent one comparison each.
        await SiteCascade.RunAsync(
            owned,
            _locks,
            _clock,
            message.AccountId,
            "stubbed",
            (siteId, _) =>
            {
                return StubAsync(siteId, message.AccountId, message.Username, webServer, limits, cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>Stubs one site's vhost, which the caller holds, unless the site has been deleted since the listing.</summary>
    /// <param name="siteId">The listed site.</param>
    /// <param name="accountId">The account being suspended.</param>
    /// <param name="username">The owning account's system user name.</param>
    /// <param name="webServer">The installed site web server.</param>
    /// <param name="limits">The account's plan bounds.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Success, or the agent's refusal.</returns>
    private async Task<Result> StubAsync(
        Guid siteId,
        Guid accountId,
        string username,
        SiteWebServer webServer,
        PhpLimitsSnapshot limits,
        CancellationToken cancellationToken)
    {
        var site = await SiteRowRefresh.ReadAsync(_dbContext, siteId, accountId, cancellationToken);
        if (site is null)
        {
            return Result.Ok();
        }

        // The descriptor comes from the row and never from literals: the suspended vhost keeps
        // the site's aliases and its log paths, and the agent decides whether a vhost is stubbed
        // by comparing what is on disk with what this same descriptor renders. A hand-built one
        // would produce a file that is suspended and does not compare equal, which the
        // attestation reports as NOT stubbed — refusing a suspension that had in fact worked.
        var stubbed = await _agent.DisableAsync(
            username, site.Domain, SiteDescriptorMapper.From(site, webServer, limits), cancellationToken);

        return stubbed.IsSuccess ? Result.Ok() : Result.Fail(stubbed.Error!);
    }
}
