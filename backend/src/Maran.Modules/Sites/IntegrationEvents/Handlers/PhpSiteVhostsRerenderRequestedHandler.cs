using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.IntegrationEvents.Events;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Sites.IntegrationEvents.Handlers;

/// <summary>
/// Renders an account's enabled PHP vhosts again, one site at a time, so a changed plan upload bound reaches
/// their request body limit now and not at the next panel start (<see cref="PhpSiteVhostsRerenderRequested"/>,
/// issue #556).
/// </summary>
/// <remarks>
/// <para>
/// <b>It can never switch a site on.</b> A suspended account's sites stay <c>Enabled</c> in their rows while
/// their vhosts are stubs, so the render is the version change with the version the row already stores, the
/// call the start-up pass makes: the agent asks whether the vhost is a suspension stub first and keeps it one.
/// The enable rpc is the deliberate un-suspend and is never used here. An account that is not active is
/// skipped before any call as well, so the stub is not even asked about.
/// </para>
/// <para>
/// <b>It does not race a disable, a delete or any other change to the site.</b> Each site is rendered under that
/// site's operation lock (<see cref="ISiteOperationLock"/>, issue #531), the same lock every command that changes the
/// site takes, so no other operation on it interleaves with the render. The rows are listed once, but each site is
/// read again after its lock is taken and skipped when it is gone, disabled or no longer PHP, so a command that
/// committed after the listing is honoured. A site that stays busy for the whole wait is not skipped silently: the
/// pass renders the others, then fails with <see cref="SiteBusyException"/> so the message is retried and the
/// render, which is idempotent, reaches the site once it is free.
/// </para>
/// <para>
/// <b>A failure never stops the others.</b> A refusal, and an exception from the agent client, are logged by
/// domain and code and the next site is rendered; only the cancellation of the message stops the pass. What it
/// misses the start-up pass converges, because both read the same rows. A busy site is the one failure that is
/// not left to the start-up pass: it is retried through the message.
/// </para>
/// </remarks>
public sealed class PhpSiteVhostsRerenderRequestedHandler
{
    /// <summary>Pre-compiled log delegate for an account whose plan or username is no longer readable.</summary>
    private static readonly Action<ILogger, Guid, Exception?> LogAccountMissing =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(1, nameof(PhpSiteVhostsRerenderRequestedHandler)),
            "Skipped the vhosts of account {AccountId} after its plan changed: the account is not readable");

    /// <summary>Pre-compiled log delegate for a vhost the agent would not render again.</summary>
    private static readonly Action<ILogger, string, string, Exception?> LogVhostFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(2, nameof(PhpSiteVhostsRerenderRequestedHandler)),
            "Could not render the vhost of {Domain} again after its plan changed: {Code}");

    /// <summary>The Sites module's database context, which holds the account's sites.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The agent, which owns the vhosts.</summary>
    private readonly IAgentSitesClient _agent;

    /// <summary>Names the installed site web server the vhosts belong to.</summary>
    private readonly SiteStackGate _stack;

    /// <summary>Reads the account without a tenant, because this runs as a queued message with no principal.</summary>
    private readonly SitePlanLimits _planLimits;

    /// <summary>The settings the account's pools carry, which the version change must send again.</summary>
    private readonly PhpAccountPoolSettings _pools;

    /// <summary>Where a failure is recorded.</summary>
    private readonly ILogger<PhpSiteVhostsRerenderRequestedHandler> _logger;

    /// <summary>Serialises the operations that change one site's host state.</summary>
    private readonly ISiteOperationLock _locks;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="agent">The agent client that renders the vhosts.</param>
    /// <param name="stack">Names the installed site web server the vhosts belong to.</param>
    /// <param name="planLimits">Reads the account without a tenant.</param>
    /// <param name="pools">The settings the account's pools carry.</param>
    /// <param name="logger">Where a failure is recorded.</param>
    /// <param name="locks">Serialises the operations that change one site's host state.</param>
    public PhpSiteVhostsRerenderRequestedHandler(
        SitesDbContext dbContext,
        IAgentSitesClient agent,
        SiteStackGate stack,
        SitePlanLimits planLimits,
        PhpAccountPoolSettings pools,
        ILogger<PhpSiteVhostsRerenderRequestedHandler> logger,
        ISiteOperationLock locks)
    {
        _locks = locks;
        _dbContext = dbContext;
        _agent = agent;
        _stack = stack;
        _planLimits = planLimits;
        _pools = pools;
        _logger = logger;
    }

    /// <summary>Renders each enabled PHP site's vhost of the account again, under its current plan bounds.</summary>
    /// <param name="message">The account whose vhosts are rendered again.</param>
    /// <param name="cancellationToken">Cancels the pass between sites.</param>
    /// <returns>A task that completes when every site was rendered, refused or skipped.</returns>
    public async Task HandleAsync(PhpSiteVhostsRerenderRequested message, CancellationToken cancellationToken)
    {
        var account = await _planLimits.FindAccountAsync(message.AccountId, cancellationToken);
        if (account is null)
        {
            LogAccountMissing(_logger, message.AccountId, null);
            return;
        }

        // A suspended account's vhosts are stubs and stay so until it is resumed, which renders them itself.
        if (account.State != AccountState.Active)
        {
            return;
        }

        // No installed web server means no vhost to render; the plan change is already committed.
        if ((await _stack.ReadAsync(cancellationToken)).WebServer is not { } webServer)
        {
            return;
        }

#pragma warning disable RS0030 // a queued message has no principal; the account's rows are found whoever changed its plan, bounded by one account id
        var siteIds = await _dbContext.Sites
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(site => site.AccountId == account.Id
                && site.BackendType == SiteBackendType.Php
                && site.Status == SiteStatus.Enabled)
            .OrderBy(site => site.Domain)
            .Select(site => site.Id)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
        if (siteIds.Count == 0)
        {
            return;
        }

        var settings = await _pools.EffectiveAsync(account.Id, account.PhpLimits, cancellationToken);
        var busy = new List<Guid>();
        foreach (var siteId in siteIds)
        {
            if (!await RenderAsync(siteId, account, webServer, settings, cancellationToken))
            {
                busy.Add(siteId);
            }
        }

        if (busy.Count > 0)
        {
            throw new SiteBusyException(
                $"{busy.Count} site(s) of account {account.Id} stayed busy, so their vhosts were not rendered again: "
                + string.Join(", ", busy));
        }
    }

    /// <summary>Renders one site's vhost again if it is still an enabled PHP site, logging anything that goes wrong.</summary>
    /// <param name="siteId">The site to render.</param>
    /// <param name="account">The account that owns it.</param>
    /// <param name="webServer">The installed site web server the vhost is rendered for.</param>
    /// <param name="settings">The settings the account's pools carry, cut to its plan.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>False when the site stayed busy for the whole wait and was not rendered; true otherwise.</returns>
    private async Task<bool> RenderAsync(
        Guid siteId,
        AccountSnapshot account,
        SiteWebServer webServer,
        IReadOnlyList<PhpSettingDto> settings,
        CancellationToken cancellationToken)
    {
        var domain = siteId.ToString();
        try
        {
            var guarded = await SiteGuard.RunAsync(
                _locks,
                siteId,
                SiteLockWait.Job,
                async () =>
                {
                    // Read again for this site, now that nothing else can change it: a disable or a delete
                    // may have committed since the listing.
                    var site = await ReadEnabledPhpSiteAsync(siteId, account.Id, cancellationToken);
                    if (site is null)
                    {
                        return Result.Ok();
                    }

                    domain = site.Domain;
                    var rendered = await _agent.ChangePhpVersionAsync(
                        account.Username,
                        site.Domain,
                        site.PhpVersion,
                        SiteDescriptorMapper.From(site, webServer, account.PhpLimits),
                        (uint)account.MaxPhpWorkersPerPool,
                        settings,
                        removePreviousPool: false,
                        cancellationToken);
                    if (!rendered.IsSuccess)
                    {
                        LogVhostFailed(_logger, site.Domain, rendered.Error!.Code, null);
                    }

                    return Result.Ok();
                },
                cancellationToken);

            return guarded.IsSuccess || !SiteBusyFailure.Is(guarded.Error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogVhostFailed(_logger, domain, exception.GetType().Name, exception);
            return true;
        }
    }

    /// <summary>Reads one site's current row, or null when it is gone, disabled or no longer PHP.</summary>
    /// <param name="siteId">The site to read.</param>
    /// <param name="accountId">The account the site must still belong to.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The row as stored now, or null when it must not be rendered.</returns>
    private async Task<Site?> ReadEnabledPhpSiteAsync(Guid siteId, Guid accountId, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // a queued message has no principal; the row is read by its id under the account the pass runs for
        return await _dbContext.Sites
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                site => site.Id == siteId
                    && site.AccountId == accountId
                    && site.BackendType == SiteBackendType.Php
                    && site.Status == SiteStatus.Enabled,
                cancellationToken);
#pragma warning restore RS0030
    }
}
