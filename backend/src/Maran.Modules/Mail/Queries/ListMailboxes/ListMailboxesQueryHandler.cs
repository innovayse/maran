using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Mail.Queries.ListMailboxes;

/// <summary>
/// Handles <see cref="ListMailboxesQuery"/> by reading one page of <c>mail.Mailboxes</c> within the
/// caller's tenant scope, and labelling each row with its owner, its status and its usage.
/// </summary>
/// <remarks>
/// <para>
/// The tenant boundary is not a <c>Where</c> clause on the caller here: the context's global query filter
/// supplies it, so this handler could not leak another account's rows even if it were rewritten
/// carelessly (spec §8). The filters narrow that scope further and are ANDed with it, and the filter
/// applies before the count as well as before the page, so a customer's total never counts another
/// tenant's rows.
/// </para>
/// <para>
/// <b>Status is a reading, not a column.</b> A mailbox is suspended because its ACCOUNT is, which the
/// Accounts module owns, so the filter asks <see cref="MailAccountStates"/> which of the accounts that
/// hold the narrowed mailboxes are suspended and expresses the three statuses over that set. The same
/// service names the status of each row on the page.
/// </para>
/// <para>
/// <b>Usage is best effort.</b> It is read from the mail host once per distinct account on the page — one
/// call per account, never one per row — and a host that cannot be asked, or has no reading for a mailbox,
/// leaves that row's <c>usedMib</c> <c>null</c>. A list that failed because the host was busy would be a
/// worse screen than one with a blank column.
/// </para>
/// </remarks>
public sealed class ListMailboxesQueryHandler
{
    /// <summary>The Mail module's database context, and this module's tenant boundary.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>The window onto the Accounts module's account names and system users, tenant-scoped.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Which accounts are suspended, which is what makes their mailboxes so.</summary>
    private readonly MailAccountStates _states;

    /// <summary>The agent, asked for mailbox usage.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>The module's display names, which name each row's status.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="accounts">The window onto the account names and system users, tenant-scoped.</param>
    /// <param name="states">Tells which accounts are suspended.</param>
    /// <param name="agent">The agent client that reports mailbox usage.</param>
    /// <param name="names">The module's display names.</param>
    public ListMailboxesQueryHandler(
        MailDbContext dbContext,
        IAccountDirectory accounts,
        MailAccountStates states,
        IAgentMailClient agent,
        IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _states = states;
        _agent = agent;
        _names = names;
    }

    /// <summary>
    /// Narrows by account, domain and status, searches the address, and answers the requested page with
    /// the total that matched and each row's owner, status and usage.
    /// </summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<MailboxDto>>> HandleAsync(
        ListMailboxesQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<Mailbox> mailboxes = _dbContext.Mailboxes
            .AsNoTracking()
            .Include(mailbox => mailbox.MailDomain);

        if (query.AccountId is { } accountId)
        {
            mailboxes = mailboxes.Where(mailbox => mailbox.AccountId == accountId);
        }

        if (!query.Domain.IsNone)
        {
            // Compared exactly, and lower-cased because the names are stored lower-cased: the filter is a
            // domain's NAME, which is what the list shows and a person types.
            var domainName = query.Domain.Value.ToLowerInvariant();
            mailboxes = mailboxes.Where(mailbox => mailbox.MailDomain.Name == domainName);
        }

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the wildcards so
            // the term matches literally. Either half of the address answers a search for it.
            var pattern = LikePattern.Contains(query.Search.Value);
            mailboxes = mailboxes.Where(mailbox =>
                EF.Functions.ILike(mailbox.LocalPart, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(mailbox.MailDomain.Name, pattern, LikePattern.EscapeCharacter));
        }

        if (query.Status is { } status)
        {
            mailboxes = await NarrowByStatusAsync(mailboxes, status, cancellationToken);
        }

        var page = await mailboxes.ToPagedAsync(query, ListMailboxesSorts.Map, cancellationToken);

        // One batched, tenant-scoped lookup for every owner on the page, never one per row.
        var accountIds = page.Items
            .Select(row =>
            {
                return row.AccountId;
            })
            .Distinct()
            .ToList();
        var ownerNames = await _accounts.NamesOfAsync(accountIds, cancellationToken);
        var suspended = await _states.SuspendedAmongAsync(accountIds, cancellationToken);
        var usage = await ReadUsageAsync(accountIds, cancellationToken);

        return Result<PagedResult<MailboxDto>>.Ok(page.Map(row =>
        {
            return MailboxMapper.From(
                row,
                row.MailDomain.Name,
                ownerNames,
                usage.TryGetValue(AddressKey(row.LocalPart, row.MailDomain.Name), out var used) ? used : null,
                MailAccountStates.MailboxStatusOf(row.Enabled, suspended.Contains(row.AccountId)),
                _names);
        }));
    }

    /// <summary>The key a mailbox is found under in the usage the host reported.</summary>
    /// <param name="localPart">The local part.</param>
    /// <param name="domainName">The domain name.</param>
    /// <returns>The lower-cased address.</returns>
    private static string AddressKey(string localPart, string domainName)
    {
        return MailboxAddressPolicy.Compose(localPart, domainName).ToLowerInvariant();
    }

    /// <summary>Narrows the mailboxes to those that read as one status.</summary>
    /// <param name="mailboxes">The mailboxes narrowed so far.</param>
    /// <param name="status">The status asked for.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The narrowed set; empty for a status no mailbox can have.</returns>
    private async Task<IQueryable<Mailbox>> NarrowByStatusAsync(
        IQueryable<Mailbox> mailboxes,
        MailboxStatus status,
        CancellationToken cancellationToken)
    {
        var holders = await mailboxes
            .Select(mailbox => mailbox.AccountId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var suspended = (await _states.SuspendedAmongAsync(holders, cancellationToken)).ToList();

        return status switch
        {
            MailboxStatus.Suspended => mailboxes.Where(mailbox => suspended.Contains(mailbox.AccountId)),
            MailboxStatus.Disabled => mailboxes.Where(mailbox =>
                !mailbox.Enabled && !suspended.Contains(mailbox.AccountId)),
            MailboxStatus.Active => mailboxes.Where(mailbox =>
                mailbox.Enabled && !suspended.Contains(mailbox.AccountId)),
            _ => mailboxes.Where(_ => false),
        };
    }

    /// <summary>Reads the usage the mail host holds for each account on the page.</summary>
    /// <param name="accountIds">The distinct accounts whose mailboxes are on the page.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>MiB used by address; a mailbox the host gave no reading for is absent.</returns>
    private async Task<Dictionary<string, long>> ReadUsageAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        var used = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var accountId in accountIds)
        {
            var account = await _accounts.FindAsync(accountId, cancellationToken);
            if (account is null)
            {
                continue;
            }

            var reading = await _agent.ListMailboxUsageAsync(account.Username, cancellationToken);
            if (!reading.IsSuccess)
            {
                continue;
            }

            foreach (var mailbox in reading.Value.Where(candidate =>
            {
                return candidate.Reading is not null;
            }))
            {
                used[AddressKey(mailbox.LocalPart, mailbox.Domain)] = MailboxMapper.MibOf(mailbox.Reading!.BytesUsed);
            }
        }

        return used;
    }
}
