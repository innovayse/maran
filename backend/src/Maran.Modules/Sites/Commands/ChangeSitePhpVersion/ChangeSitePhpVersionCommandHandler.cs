using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
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
    /// <summary>Customer php.ini overrides, of which the panel stores none in this pass.</summary>
    /// <remarks>
    /// NOT a fabricated value standing in for something the panel knows: there is no override
    /// storage in this module, so "none" is the true and complete set. When overrides gain a home,
    /// this constant is the single place that has to start reading it.
    /// </remarks>
    private static readonly IReadOnlyList<PhpSettingDto> NoSettingOverrides = [];

    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The owning account's system user name and its plan's per-pool worker budget.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns the vhost and the pool.</summary>
    private readonly IAgentSitesClient _agent;

    /// <summary>The host's PHP runtimes, so a site is never rebound to a version that is not installed.</summary>
    private readonly IAgentPhpClient _php;

    /// <summary>This module's audit journal.</summary>
    private readonly SiteAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="accounts">The owning account's system user name and per-pool worker budget.</param>
    /// <param name="agent">The agent client that re-renders the pool and the vhost.</param>
    /// <param name="php">The agent client listing the host's installed PHP runtimes.</param>
    /// <param name="journal">This module's audit journal.</param>
    public ChangeSitePhpVersionCommandHandler(
        SitesDbContext dbContext,
        IAccountDirectory accounts,
        IAgentSitesClient agent,
        IAgentPhpClient php,
        SiteAuditJournal journal)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _php = php;
        _journal = journal;
    }

    /// <summary>Rebinds the site's PHP version, refusing a version the host does not have.</summary>
    /// <param name="command">Which site, and which version.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The site in its new state, or <c>SiteNotFound</c>, <c>SiteBackendNotPhp</c>,
    /// <c>AccountNotFound</c>, <c>PhpVersionNotInstalled</c>, or the agent's own typed failure.
    /// </returns>
    public async Task<Result<SiteDto>> HandleAsync(
        ChangeSitePhpVersionCommand command,
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

        var versions = await _php.ListVersionsAsync(cancellationToken);
        if (!versions.IsSuccess)
        {
            return await FailAsync(command, site.Domain, versions.Error!, cancellationToken);
        }

        // "The agent could not be asked" and "the version is not installed" are different answers
        // and are not conflated: the first is retried, the second is a fact the customer must act on.
        var installed = versions.Value.Any(version =>
        {
            return string.Equals(version.Version, command.PhpVersion, StringComparison.Ordinal);
        });
        if (!installed)
        {
            return await FailAsync(command, site.Domain, Error.Of(nameof(ErrorMessages.PhpVersionNotInstalled), ErrorType.Validation), cancellationToken);
        }

        // The descriptor is built from the STORED row, never assembled here, so the re-rendered
        // vhost is the site's own — the same aliases, the same backend, and the same TLS block it
        // already had (see SiteDescriptorMapper).
        var rebound = await _agent.ChangePhpVersionAsync(
            account.Username,
            site.Domain,
            command.PhpVersion,
            SiteDescriptorMapper.From(site),
            (uint)account.MaxPhpWorkersPerPool,
            NoSettingOverrides,
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
            AuditActions.SitePhpVersionChanged, site.Domain, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<SiteDto>.Ok(new SiteDto(
            site.Id, site.AccountId, site.Domain, site.BackendType, site.PhpVersion, site.Status, site.CreatedAt));
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
            AuditActions.SitePhpVersionChanged, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<SiteDto>.Fail(error);
    }
}
