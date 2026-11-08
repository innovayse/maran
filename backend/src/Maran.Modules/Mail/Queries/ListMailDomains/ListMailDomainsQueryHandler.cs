using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Mail.Queries.ListMailDomains;

/// <summary>
/// Handles <see cref="ListMailDomainsQuery"/>: one page of mail domains, each with its owner's name,
/// derived status and mailbox count.
/// </summary>
/// <remarks>
/// <para>
/// Tenant scope is the context's query filter and nothing in this handler: there is no <c>Where</c>
/// on the caller, so a customer cannot be shown a neighbour's domain by a missing predicate.
/// </para>
/// <para>
/// A domain's status is not stored — it is the state of its account — so the status filter first asks
/// which of the accounts that own the otherwise-filtered domains are suspended, then keeps or drops
/// domains by that set. The mailbox counts of the page are one grouped query, never one per row.
/// </para>
/// </remarks>
public sealed class ListMailDomainsQueryHandler
{
    /// <summary>The Mail module's database context, and this module's tenant boundary.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>The window onto account names.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Says which accounts are suspended.</summary>
    private readonly MailAccountStates _states;

    /// <summary>The module's localized display names.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="accounts">Resolves owner names in one batch.</param>
    /// <param name="states">Says which accounts are suspended.</param>
    /// <param name="names">The module's localized display names.</param>
    public ListMailDomainsQueryHandler(
        MailDbContext dbContext,
        IAccountDirectory accounts,
        MailAccountStates states,
        IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _states = states;
        _names = names;
    }

    /// <summary>Returns the requested page.</summary>
    /// <param name="query">Filter, sort and paging parameters.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page of domains.</returns>
    public async Task<Result<PagedResult<MailDomainDto>>> HandleAsync(
        ListMailDomainsQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<MailDomain> domains = _dbContext.MailDomains
            .AsNoTracking()
            .Include(domain => domain.DkimKeys);

        if (query.AccountId is { } accountId)
        {
            domains = domains.Where(domain => domain.AccountId == accountId);
        }

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the wildcards
            // so the term matches literally.
            var pattern = LikePattern.Contains(query.Search.Value);
            domains = domains.Where(domain =>
                EF.Functions.ILike(domain.Name, pattern, LikePattern.EscapeCharacter));
        }

        if (query.Status is { } status)
        {
            domains = await FilterByStatusAsync(domains, status, cancellationToken);
        }

        var page = await domains.ToPagedAsync(query, ListMailDomainsSorts.Map, cancellationToken);

        var ownerIds = page.Items
            .Select(row =>
            {
                return row.AccountId;
            })
            .Distinct()
            .ToList();
        var ownerNames = await _accounts.NamesOfAsync(ownerIds, cancellationToken);
        var suspended = await _states.SuspendedAmongAsync(ownerIds, cancellationToken);

        var domainIds = page.Items
            .Select(row =>
            {
                return row.Id;
            })
            .ToList();
        var mailboxCounts = await _dbContext.Mailboxes
            .AsNoTracking()
            .Where(mailbox => domainIds.Contains(mailbox.MailDomainId))
            .GroupBy(mailbox => mailbox.MailDomainId)
            .Select(group => new { DomainId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group =>
            {
                return group.DomainId;
            }, group =>
            {
                return group.Count;
            }, cancellationToken);

        return Result<PagedResult<MailDomainDto>>.Ok(page.Map(row =>
        {
            return MailDomainMapper.From(
                row,
                ownerNames,
                MailAccountStates.DomainStatusOf(suspended.Contains(row.AccountId)),
                mailboxCounts.GetValueOrDefault(row.Id),
                _names);
        }));
    }

    /// <summary>Keeps the domains whose account reads as <paramref name="status"/>.</summary>
    /// <param name="domains">The domains already narrowed by every other filter.</param>
    /// <param name="status">The status to keep.</param>
    /// <param name="cancellationToken">Cancels the account read.</param>
    /// <returns>The narrowed query.</returns>
    private async Task<IQueryable<MailDomain>> FilterByStatusAsync(
        IQueryable<MailDomain> domains,
        MailDomainStatus status,
        CancellationToken cancellationToken)
    {
        var owners = await domains
            .Select(domain => domain.AccountId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var suspended = await _states.SuspendedAmongAsync(owners, cancellationToken);
        var suspendedIds = suspended.ToList();

        return status == MailDomainStatus.Suspended
            ? domains.Where(domain => suspendedIds.Contains(domain.AccountId))
            : domains.Where(domain => !suspendedIds.Contains(domain.AccountId));
    }
}
