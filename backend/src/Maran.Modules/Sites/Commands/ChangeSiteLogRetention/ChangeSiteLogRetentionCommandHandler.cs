using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.IntegrationEvents.Events;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Wolverine;

namespace Maran.Modules.Sites.Commands.ChangeSiteLogRetention;

/// <summary>
/// Handles <see cref="ChangeSiteLogRetentionCommand"/>: stores how many days of rotated logs one site
/// keeps, records the change, and asks the maintenance pass to apply it.
/// </summary>
/// <remarks>
/// <para>
/// <b>It never calls the agent.</b> A customer's request is not allowed to become root work, so the row
/// is saved, the change is journalled, and a <see cref="SiteLogRetentionChanged"/> message is published
/// after the commit; the archives are removed by the next maintenance pass or by that message's handler.
/// The answer therefore never waits on the agent, and a stopped agent cannot make the save fail.
/// </para>
/// <para>
/// <b>The guarantee relied on:</b> the message is published after the commit and is not enlisted in it.
/// If the process dies in that window the message is lost, and the hourly pass still prunes every site to
/// its stored number, so nothing is lost but time (<see cref="SiteLogRetentionChanged"/> says why that is
/// acceptable).
/// </para>
/// <para>
/// The site is loaded through the tenant-filtered context, so another customer's site is not found:
/// <c>404</c>, never <c>403</c>.
/// </para>
/// </remarks>
public sealed class ChangeSiteLogRetentionCommandHandler
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>Names the owning account for the answer.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>This module's audit journal.</summary>
    private readonly SiteAuditJournal _journal;

    /// <summary>Names the site's backend and serving state in the request's culture.</summary>
    private readonly SiteDisplayNames _siteNames;

    /// <summary>The message bus the follow-up message is published on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="accounts">Names the owning account for the answer.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="siteNames">Names the site's backend and serving state for the request's culture.</param>
    /// <param name="bus">The message bus the follow-up message is published on.</param>
    public ChangeSiteLogRetentionCommandHandler(
        SitesDbContext dbContext,
        IAccountDirectory accounts,
        SiteAuditJournal journal,
        SiteDisplayNames siteNames,
        IMessageBus bus)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _journal = journal;
        _siteNames = siteNames;
        _bus = bus;
    }

    /// <summary>Stores the site's new retention.</summary>
    /// <param name="command">Which site, and how many days.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The site in its new state, or <c>SiteNotFound</c>.</returns>
    public async Task<Result<SiteDto>> HandleAsync(
        ChangeSiteLogRetentionCommand command,
        CancellationToken cancellationToken)
    {
        var site = await _dbContext.Sites.SingleOrDefaultAsync(s => s.Id == command.SiteId, cancellationToken);
        if (site is null)
        {
            // The subject is the identifier the caller supplied, because no domain is known: a probe
            // for a site the caller may not see still leaves a trace naming what was probed for.
            await _journal.RecordFailureAsync(
                SiteAuditActions.SiteLogRetentionChanged,
                command.SiteId.ToString(),
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<SiteDto>.Fail(Error.Of(nameof(ErrorMessages.SiteNotFound), ErrorType.NotFound));
        }

        site.ChangeLogRetention(command.Days);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            SiteAuditActions.SiteLogRetentionChanged, site.Domain, command.IpAddress, command.UserAgent, cancellationToken);

        // After the commit, on purpose; see the class remarks for the guarantee this relies on.
        await _bus.PublishAsync(new SiteLogRetentionChanged(site.Id));

        var owners = await _accounts.NamesOfAsync([site.AccountId], cancellationToken);

        return Result<SiteDto>.Ok(new SiteDto(
            site.Id,
            site.AccountId,
            owners.GetValueOrDefault(site.AccountId, string.Empty),
            site.Domain,
            site.BackendType,
            _siteNames.Of(site.BackendType),
            site.PhpVersion,
            site.Status,
            _siteNames.Of(site.Status),
            site.CreatedAt,
            site.LogRetentionDays));
    }
}
