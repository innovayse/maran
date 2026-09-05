using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Commands.EnableSite;

/// <summary>Handles <see cref="EnableSiteCommand"/> by restoring normal serving for the site on the host, then recording it.</summary>
/// <remarks>
/// The agent runs first and the row is written only if it succeeded, the same order every other
/// mutation in this module uses: the panel understating what it has already done on the host is a
/// wrong label, where the reverse is a customer told their site is in a state it is not.
///
/// The site is loaded through the tenant-filtered <see cref="SitesDbContext.Sites"/>, so another
/// customer's site is not found at all — a 404, never a 403, because a 403 would confirm the site
/// exists (rules/testing.md). Every refusal is journalled alongside every success.
/// </remarks>
public sealed class EnableSiteCommandHandler
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
    /// <param name="agent">The agent client that performs the privileged half.</param>
    /// <param name="journal">This module's audit journal.</param>
    public EnableSiteCommandHandler(
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

    /// <summary>Applies the change. Idempotent: repeating it changes nothing.</summary>
    /// <param name="command">Which site to act on.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The site in its new state, or <c>SiteNotFound</c>, <c>AccountNotFound</c>, or the agent's own typed failure.</returns>
    public async Task<Result<SiteDto>> HandleAsync(EnableSiteCommand command, CancellationToken cancellationToken)
    {
        var site = await _dbContext.Sites.SingleOrDefaultAsync(s => s.Id == command.SiteId, cancellationToken);
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

        // The descriptor comes from the stored row, so the re-rendered vhost keeps the site's own
        // aliases, backend, upstream and TLS block (see SiteDescriptorMapper).
        var applied = await _agent.EnableAsync(
            account.Username,
            site.Domain,
            SiteDescriptorMapper.From(site),
            cancellationToken);
        if (!applied.IsSuccess)
        {
            return await FailAsync(command, site.Domain, applied.Error!, cancellationToken);
        }

        site.Enable();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.SiteEnabled, site.Domain, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<SiteDto>.Ok(new SiteDto(
            site.Id, site.AccountId, site.Domain, site.BackendType, site.PhpVersion, site.Status, site.CreatedAt));
    }

    /// <summary>Journals a refused operation and returns it as the typed failure.</summary>
    /// <param name="command">The operation that was refused.</param>
    /// <param name="subject">The site's domain, or the supplied identifier when no site was found.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<SiteDto>> FailAsync(
        EnableSiteCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.SiteEnabled, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<SiteDto>.Fail(error);
    }
}
