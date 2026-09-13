using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Commands.DeleteSite;

/// <summary>
/// Handles <see cref="DeleteSiteCommand"/> by removing the site's vhost from the host and then the
/// row that defined it. The customer's files are left where they are.
/// </summary>
/// <remarks>
/// The agent runs first, as everywhere else in this module. Deleting is the case where the order
/// matters most: a vhost removed with the row still present is a site the panel still lists and a
/// later retry converges, while a row removed with the vhost still serving is a site nobody in the
/// panel can see and nobody can now take down.
///
/// The site is loaded through the tenant-filtered <see cref="SitesDbContext.Sites"/>, so another
/// customer's site answers 404 rather than 403 (rules/testing.md). Refusals are journalled: a
/// deletion someone attempted and was refused is exactly what an operator later wants to find.
/// </remarks>
public sealed class DeleteSiteCommandHandler
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The owning account's system user name, which addresses every agent operation.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns the vhost.</summary>
    private readonly IAgentSitesClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly SiteAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that removes the vhost.</param>
    /// <param name="journal">This module's audit journal.</param>
    public DeleteSiteCommandHandler(
        SitesDbContext dbContext,
        IAccountDirectory accounts,
        IAgentSitesClient agent,
        SiteAuditJournal journal)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
    }

    /// <summary>Removes the site. Idempotent: the agent reports a missing vhost as success.</summary>
    /// <param name="command">Which site to remove.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or <c>SiteNotFound</c>, <c>AccountNotFound</c>, or the agent's own typed failure.</returns>
    public async Task<Result<bool>> HandleAsync(DeleteSiteCommand command, CancellationToken cancellationToken)
    {
        // The hostname claims are loaded with the site so that removing it frees the names it held
        // through the tracked graph, rather than relying on the database's cascade alone: a name
        // still claimed by a deleted site is a name its owner can never use again.
        var site = await _dbContext.Sites
            .Include(s => s.Hostnames)
            .SingleOrDefaultAsync(s => s.Id == command.SiteId, cancellationToken);
        if (site is null)
        {
            // The subject is the identifier the caller supplied, because no domain is known — a
            // probe for a site the caller may not see still leaves a trace naming what was probed for.
            return await FailAsync(command, command.SiteId.ToString(), Error.Of(nameof(ErrorMessages.SiteNotFound), ErrorType.NotFound), cancellationToken);
        }

        var account = await _accounts.FindAsync(site.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(command, site.Domain, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        // Whether the site's php-fpm pool may go with it is a question only the panel can answer,
        // and it is answered from the rows rather than from anything on the host: a pool belongs to
        // an ACCOUNT and a version, and two of the account's sites on the same version share one
        // pool and one worker budget. The agent holds a directory of rendered vhosts, which is a
        // rendering of these rows and not a second copy to count (rules/architecture.md).
        var removed = await _agent.DeleteAsync(
            account.Username,
            site.Domain,
            await RetiredPhpVersionAsync(site, cancellationToken),
            cancellationToken);
        if (!removed.IsSuccess)
        {
            return await FailAsync(command, site.Domain, removed.Error!, cancellationToken);
        }

        // Captured before the row is removed: the journal records the domain, which is the only
        // thing about a deleted site anybody will later be able to search for.
        var domain = site.Domain;

        _dbContext.Sites.Remove(site);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.SiteDeleted, domain, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <summary>
    /// The PHP version whose pool this deletion retires, or the empty string when none does.
    /// </summary>
    /// <param name="site">The site being deleted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The version to retire, or the empty string.</returns>
    /// <remarks>
    /// Empty for a site that is not PHP-backed, and empty while the account keeps ANOTHER site on
    /// the same version — which is the case that matters, because getting it wrong takes that other
    /// site off the air rather than merely leaving a file behind.
    ///
    /// <c>IgnoreQueryFilters</c> is deliberately NOT used: the account has just been resolved
    /// through the tenant-scoped directory, so the caller demonstrably owns it and the filter is
    /// already answering for the right tenant. A bypass here would be decoration that no test could
    /// distinguish from its own absence.
    /// </remarks>
    private async Task<string> RetiredPhpVersionAsync(Site site, CancellationToken cancellationToken)
    {
        if (site.BackendType != SiteBackendType.Php || string.IsNullOrEmpty(site.PhpVersion))
        {
            return string.Empty;
        }

        var stillUsed = await _dbContext.Sites.AnyAsync(
            other => other.AccountId == site.AccountId
                && other.Id != site.Id
                && other.PhpVersion == site.PhpVersion,
            cancellationToken);

        return stillUsed ? string.Empty : site.PhpVersion;
    }

    /// <summary>Journals a refused deletion and returns it as the typed failure.</summary>
    /// <param name="command">The deletion that was refused.</param>
    /// <param name="subject">The site's domain, or the supplied identifier when no site was found.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<bool>> FailAsync(
        DeleteSiteCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.SiteDeleted, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<bool>.Fail(error);
    }
}
