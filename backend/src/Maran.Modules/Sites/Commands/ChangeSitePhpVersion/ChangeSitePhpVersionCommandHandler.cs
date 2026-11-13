using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Commands.ChangeSitePhpVersion;

/// <summary>
/// Handles <see cref="ChangeSitePhpVersionCommand"/>: rebinds a PHP-backed site to a different
/// installed version, re-rendering its pool and its vhost, and then records the change.
/// </summary>
/// <remarks>
/// The site is loaded through the tenant-filtered <see cref="SitesDbContext.Sites"/>, so another
/// customer's site is simply not found. That is deliberately a 404 and not a 403: a 403 would
/// confirm the site exists, which is all an attacker needs to enumerate other people's domains
/// (rules/testing.md). Every refusal, including that one, is journalled.
/// </remarks>
public sealed class ChangeSitePhpVersionCommandHandler
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The owning account's system user name and its plan's per-pool worker budget.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns the vhost and the pool.</summary>
    private readonly IAgentSitesClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly SiteAuditJournal _journal;

    /// <summary>Names the site's backend and serving state in the request's culture.</summary>
    private readonly SiteDisplayNames _siteNames;

    /// <summary>
    /// The settings the account's pools carry. The new version's pool is written with them, so the
    /// customer's choices follow the site to the version it moves to.
    /// </summary>
    private readonly PhpAccountPoolSettings _poolSettings;

    /// <summary>Refuses the operation while no site web server is installed.</summary>
    private readonly SiteStackGate _stack;

    /// <summary>Serialises the operations that change one site's host state.</summary>
    private readonly ISiteOperationLock _locks;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="accounts">The owning account's system user name and per-pool worker budget.</param>
    /// <param name="agent">The agent client that re-renders the pool and the vhost.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="siteNames">Names the site's backend and serving state for the request's culture.</param>
    /// <param name="poolSettings">The settings the account's pools carry, sent with every pool write.</param>
    /// <param name="stack">Refuses the operation while no site web server is installed.</param>
    /// <param name="locks">Serialises the operations that change one site's host state.</param>
    public ChangeSitePhpVersionCommandHandler(
        SitesDbContext dbContext,
        IAccountDirectory accounts,
        IAgentSitesClient agent,
        SiteAuditJournal journal,
        SiteDisplayNames siteNames,
        PhpAccountPoolSettings poolSettings,
        SiteStackGate stack,
        ISiteOperationLock locks)
    {
        _locks = locks;
        _stack = stack;
        _dbContext = dbContext;
        _siteNames = siteNames;
        _poolSettings = poolSettings;
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
    }

    /// <summary>Rebinds the site's PHP version, refusing a version the host does not have.</summary>
    /// <param name="command">Which site, and which version.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The site in its new state, or <c>SiteNotFound</c>, <c>SiteBackendNotPhp</c>,
    /// <c>AccountNotFound</c>, <c>AppsStackNotReady</c>, <c>AppsNotInstalled</c>, or the agent's own typed failure.
    /// </returns>
    public async Task<Result<SiteDto>> HandleAsync(
        ChangeSitePhpVersionCommand command,
        CancellationToken cancellationToken)
    {
        // The stack gate: sites cannot be changed while no web server is installed. Enforced here and
        // not in the SPA, which only reflects it (docs/apps/README.md).
        var installed = await _stack.ReadAsync(cancellationToken);
        if (installed.NotReadyFailure() is { } notReady)
        {
            return await FailAsync(command, command.SiteId.ToString(), notReady, cancellationToken);
        }

        // The site is held for the whole change, from reading its row to saving it, so no other
        // operation on it interleaves; the row is read inside the guard for that reason (#531).
        var guarded = await SiteGuard.RunAsync(
            _locks,
            command.SiteId,
            SiteLockWait.Interactive,
            () =>
            {
                return ApplyAsync(command, installed, cancellationToken);
            },
            cancellationToken);

        return !guarded.IsSuccess && SiteBusyFailure.Is(guarded.Error)
            ? await FailAsync(command, command.SiteId.ToString(), guarded.Error!, cancellationToken)
            : guarded;
    }

    /// <summary>Applies the change to the site, which the caller holds.</summary>
    /// <param name="command">Which site to act on.</param>
    /// <param name="installed">The apps the stack gate found installed.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The change's outcome, or the typed failure.</returns>
    private async Task<Result<SiteDto>> ApplyAsync(
        ChangeSitePhpVersionCommand command,
        SiteHostApps installed,
        CancellationToken cancellationToken)
    {
        var site = await _dbContext.Sites.SingleOrDefaultAsync(s => s.Id == command.SiteId, cancellationToken);
        if (site is null)
        {
            // The subject is the identifier the caller supplied, because no domain is known — a
            // probe for a site the caller may not see still leaves a trace naming what was probed for.
            return await FailAsync(command, command.SiteId.ToString(), Error.Of(nameof(ErrorMessages.SiteNotFound), ErrorType.NotFound), cancellationToken);
        }

        // A site whose backend is not PHP has no PHP version to change. Without this the agent is
        // asked to render an FPM pool for a static site while being handed a descriptor that says
        // Static, and the row ends up claiming BackendType=Static with a PhpVersion set — a state
        // no renderer can make sense of and nothing else in the module would ever produce.
        if (site.BackendType != SiteBackendType.Php)
        {
            return await FailAsync(command, site.Domain, Error.Of(nameof(ErrorMessages.SiteBackendNotPhp), ErrorType.Validation), cancellationToken);
        }

        var account = await _accounts.FindAsync(site.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(command, site.Domain, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        // The version must be an installed PHP app: the apps are what the host has, and a site bound to a
        // runtime nobody installed would be born pointing at a socket nothing has bound. The stack was
        // read once at the top, so this is a fact about the apps and not about the agent's reachability.
        if (!installed.HasPhp(command.PhpVersion))
        {
            return await FailAsync(command, site.Domain, SiteHostApps.NotInstalledFailure(), cancellationToken);
        }

        // The pools of the account are held from the question "is this the last site on the version being
        // left" to the save (#561): a pool belongs to an account and a version, so a delete of ANOTHER site
        // that is last on the version this one moves to would otherwise retire the pool this call has just
        // created. The site's own lock, held by the caller, cannot cover it.
        return await PhpPoolGuard.RunAsync(
            _locks,
            site.AccountId,
            SiteLockWait.Interactive,
            () =>
            {
                return RebindAsync(command, installed, site, account, cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>Renders the new version and records it, for a site and an account pool the caller holds.</summary>
    /// <param name="command">Which site, and which version.</param>
    /// <param name="installed">The apps the stack gate found installed.</param>
    /// <param name="site">The tracked row, still carrying its OLD version.</param>
    /// <param name="account">The owning account.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The site in its new state, or the typed failure.</returns>
    private async Task<Result<SiteDto>> RebindAsync(
        ChangeSitePhpVersionCommand command,
        SiteHostApps installed,
        Site site,
        AccountSnapshot account,
        CancellationToken cancellationToken)
    {
        // The descriptor is built from the STORED row, never assembled here, so the re-rendered
        // vhost is the site's own — the same aliases, the same backend, and the same TLS block it
        // already had (see SiteDescriptorMapper).
        var rebound = await _agent.ChangePhpVersionAsync(
            account.Username,
            site.Domain,
            command.PhpVersion,
            SiteDescriptorMapper.From(site, installed.WebServer!.Value, account.PhpLimits),
            (uint)account.MaxPhpWorkersPerPool,
            await _poolSettings.EffectiveAsync(site.AccountId, account.PhpLimits, cancellationToken),
            // Whether the version being LEFT may lose its pool, answered from the panel's own rows:
            // a pool belongs to an account and a version, so removing it because this one site
            // moved would take the account's other sites on the old version off the air. False
            // leaves the old pool standing, which is safe and merely wasteful.
            await LeavingLastSiteOnVersionAsync(site, cancellationToken),
            cancellationToken);
        if (!rebound.IsSuccess)
        {
            return await FailAsync(command, site.Domain, rebound.Error!, cancellationToken);
        }

        site.ChangePhpVersion(command.PhpVersion);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            SiteAuditActions.SitePhpVersionChanged, site.Domain, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<SiteDto>.Ok(new SiteDto(
            site.Id,
            site.AccountId,
            account.Username,
            site.Domain,
            site.BackendType,
            _siteNames.Of(site.BackendType),
            site.PhpVersion,
            site.Status,
            _siteNames.Of(site.Status),
            site.CreatedAt,
            site.LogRetentionDays));
    }

    /// <summary>Whether this site is the account's last one on the version it is leaving.</summary>
    /// <param name="site">The site being switched, still carrying its OLD version.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><c>true</c> when nothing else needs the old version's pool.</returns>
    /// <remarks>
    /// Read BEFORE the row is updated, which is why it is called at the agent call site rather than
    /// after it: once <c>ChangePhpVersion</c> has run, <c>site.PhpVersion</c> is the NEW version and
    /// the question has quietly become a different one — "is this the last site on the version it
    /// just moved to" — whose answer is usually the opposite.
    /// </remarks>
    private async Task<bool> LeavingLastSiteOnVersionAsync(Site site, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(site.PhpVersion))
        {
            return false;
        }

        var stillUsed = await _dbContext.Sites.AnyAsync(
            other => other.AccountId == site.AccountId
                && other.Id != site.Id
                && other.PhpVersion == site.PhpVersion,
            cancellationToken);

        return !stillUsed;
    }

    /// <summary>Journals a refused rebind and returns it as the typed failure.</summary>
    /// <param name="command">The rebind that was refused.</param>
    /// <param name="subject">The site's domain, or the supplied identifier when no site was found.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<SiteDto>> FailAsync(
        ChangeSitePhpVersionCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            SiteAuditActions.SitePhpVersionChanged, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<SiteDto>.Fail(error);
    }
}
