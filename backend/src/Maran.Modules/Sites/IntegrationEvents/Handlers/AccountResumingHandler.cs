using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.IntegrationEvents.Handlers;

/// <summary>
/// Restores the sites an account had ENABLED when it is about to be reactivated
/// (<see cref="AccountResuming"/>), and leaves the ones the customer had disabled on the stub.
/// </summary>
/// <remarks>
/// <para>
/// <b>The status filter is the whole point of this handler.</b> <see cref="AccountSuspendingHandler"/>
/// stubs every vhost the account owns and writes nothing, so after a suspension the host cannot tell
/// a site the customer disabled from a site the suspension disabled — both are serving the same
/// suspended vhost, byte for byte. Only <c>Site.Status</c> in this panel's own table records which
/// was which. So the reversal is asymmetric on purpose: everything is stubbed, and only
/// <see cref="SiteStatus.Enabled"/> comes back.
/// </para>
/// <para>
/// <b>Why the agent cannot do this instead.</b> It is the argument for orchestrating a suspension as
/// an event per module rather than as one agent rpc. The agent holds no <c>Site.Status</c> and has
/// no way to acquire it — a "resume everything" on the host would silently re-enable a site the
/// customer had turned off, which is the panel overwriting a customer's own decision while claiming
/// to restore it. Stated as the law it is an instance of: suspension must not overwrite state that
/// also expresses a customer choice, so resumption may only restore what suspension changed.
/// </para>
/// <para>
/// <b>The failure is not swallowed</b>, for the reason its sibling gives: anything thrown here
/// aborts the reactivation and leaves the account suspended, which is recoverable. An account marked
/// active whose sites still show the suspension page is the panel telling a paying customer their
/// service is back when it is not.
/// </para>
/// </remarks>
public sealed class AccountResumingHandler
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
    public AccountResumingHandler(SitesDbContext dbContext, IAgentSitesClient agent, SiteStackGate stack, SitePlanLimits planLimits,
        ISiteOperationLock locks, IClock clock)
    {
        _clock = clock;
        _locks = locks;
        _planLimits = planLimits;
        _dbContext = dbContext;
        _agent = agent;
        _stack = stack;
    }

    /// <summary>Restores the site's own vhost for every site the account had enabled.</summary>
    /// <remarks>
    /// The tenant query filter is bypassed for the reason <see cref="AccountSuspendingHandler"/>
    /// gives: this is the reactivation of the account the rows belong to, authorised before the
    /// event was invoked, and a filtered query would silently restore nothing the day reactivation
    /// stops being an administrator's operation.
    /// </remarks>
    /// <param name="message">The account about to be reactivated.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="InvalidOperationException">
    /// The agent refused to restore a vhost. Thrown rather than returned, because failing is a
    /// subscriber's only way to abort the reactivation.
    /// </exception>
    public async Task HandleAsync(AccountResuming message, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // the account is being reactivated, so its rows must be found whoever asked for it
        var enabled = await _dbContext.Sites
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(site => site.AccountId == message.AccountId && site.Status == SiteStatus.Enabled)
            .OrderBy(site => site.Domain)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030

        if (enabled.Count == 0)
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
        // Every site is tried before this fails. The account stays suspended, which is the recoverable
        // direction, and repeating the reactivation is the retry: the restore is idempotent. A site is
        // restored only if the row, read again under its lock, is still enabled: a disable that committed
        // since the listing is the customer's choice and must stay a stub (#531).
        await SiteCascade.RunAsync(
            enabled,
            _locks,
            _clock,
            message.AccountId,
            "restored",
            (siteId, _) =>
            {
                return RestoreAsync(siteId, message.AccountId, message.Username, webServer, limits, cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>Restores one site's vhost, which the caller holds, if the site is still enabled.</summary>
    /// <param name="siteId">The listed site.</param>
    /// <param name="accountId">The account being resumed.</param>
    /// <param name="username">The owning account's system user name.</param>
    /// <param name="webServer">The installed site web server.</param>
    /// <param name="limits">The account's plan bounds.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Success, or the agent's refusal.</returns>
    private async Task<Result> RestoreAsync(
        Guid siteId,
        Guid accountId,
        string username,
        SiteWebServer webServer,
        PhpLimitsSnapshot limits,
        CancellationToken cancellationToken)
    {
        var site = await SiteRowRefresh.ReadAsync(_dbContext, siteId, accountId, cancellationToken);
        if (site is null || site.Status != SiteStatus.Enabled)
        {
            return Result.Ok();
        }

        var restored = await _agent.EnableAsync(
            username, site.Domain, SiteDescriptorMapper.From(site, webServer, limits), cancellationToken);

        return restored.IsSuccess ? Result.Ok() : Result.Fail(restored.Error!);
    }
}
