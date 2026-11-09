using Maran.Modules.Mail.Persistence;
using Maran.Sdk.Events;

namespace Maran.Modules.Mail.IntegrationEvents.Handlers;

/// <summary>
/// Removes this module's rows for an account that is about to be deleted (<see cref="AccountDeleting"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> System user names are recycled, so an account created again under the same name
/// would find the previous tenant's mail domains, mailboxes and aliases listed as its own, and a mail domain
/// would stay claimed by an account that no longer exists.
/// </para>
/// <para>
/// <b>This removes ROWS, not mail.</b> The host-side teardown (the mailboxes, their Maildirs, the DKIM keys
/// and the rendered configuration, all before the system user is removed) is the agent's, driven from the same
/// account deletion; it reads the machine rather than replaying this table, for the reason the suspension
/// handler gives.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here propagates to the Accounts handler, which abandons
/// the deletion with the account intact — the recoverable half.
/// </para>
/// <para>The module's settings row is untouched: it belongs to the installation, not to any account.</para>
/// </remarks>
public sealed class AccountDeletingHandler
{
    /// <summary>The Mail module's database context.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    public AccountDeletingHandler(MailDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Deletes every mail row belonging to the account being removed.</summary>
    /// <remarks>
    /// The tenant query filter is deliberately bypassed in each read: the filter governs what a REQUEST may
    /// see, and this is the removal of the account the rows belong to, authorised before the event was
    /// published. The reads are explicit about every table rather than relying on the database cascade, so the
    /// removal is the same on every provider and a row left behind is a failure here and not a surprise later.
    /// </remarks>
    /// <param name="message">The account about to be deleted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task HandleAsync(AccountDeleting message, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // the account is being deleted, so its rows must be found whoever asked for the deletion
        var responders = await _dbContext.MailAutoresponders
            .IgnoreQueryFilters()
            .Where(row => row.Mailbox.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
        var aliases = await _dbContext.MailAliases
            .IgnoreQueryFilters()
            .Include(row => row.Targets)
            .Where(row => row.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
        var mailboxes = await _dbContext.Mailboxes
            .IgnoreQueryFilters()
            .Where(row => row.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
        var domains = await _dbContext.MailDomains
            .IgnoreQueryFilters()
            .Include(row => row.DkimKeys)
            .Where(row => row.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
        if (responders.Count + aliases.Count + mailboxes.Count + domains.Count == 0)
        {
            return;
        }

        _dbContext.MailAutoresponders.RemoveRange(responders);
        _dbContext.MailAliasTargets.RemoveRange(aliases.SelectMany(alias =>
        {
            return alias.Targets;
        }));
        _dbContext.MailAliases.RemoveRange(aliases);
        _dbContext.Mailboxes.RemoveRange(mailboxes);
        _dbContext.DkimKeys.RemoveRange(domains.SelectMany(domain =>
        {
            return domain.DkimKeys;
        }));
        _dbContext.MailDomains.RemoveRange(domains);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
