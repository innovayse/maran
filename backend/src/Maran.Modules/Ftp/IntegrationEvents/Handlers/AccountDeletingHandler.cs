using Maran.Modules.Ftp.Persistence;
using Maran.Sdk.Events;

namespace Maran.Modules.Ftp.IntegrationEvents.Handlers;

/// <summary>
/// Removes this module's login rows for an account that is about to be deleted
/// (<see cref="AccountDeleting"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> Deleting an account does not, on its own, remove an
/// <see cref="Maran.Modules.Ftp.Domain.Entities.FtpUser"/> row. System user names are recycled, so an
/// account created again under the same name would find the previous tenant's logins listed in the
/// panel as its own.
/// </para>
/// <para>
/// <b>This removes ROWS, not logins.</b> The host-side teardown is the agent's, driven from the same
/// account deletion: it revokes every login the PASSWORD DATABASE holds for the account, takes the
/// jail's bind mount down and removes the jail. It reads the machine rather than replaying this
/// table, and that is the whole reason the cascade is not a loop here — a list the panel remembers
/// can only describe the logins it created, and a login it has forgotten is exactly the one still
/// letting a deleted customer's name back in.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here propagates to the Accounts handler,
/// which abandons the deletion with the account intact — the recoverable half.
/// </para>
/// <para>
/// <b>There is deliberately no suspending or resuming handler beside this one, and the absence is a
/// decision rather than an omission</b> — the two look identical in a folder listing, which is why it
/// is written down. The agent's <c>SetAccountLoginsLocked</c> already covers both transfer
/// protocols, and the Sftp module already drives it on the same events. A second module calling it
/// would lock the same logins twice and race on the resume.
/// </para>
/// <para>
/// The module's settings row is untouched: it belongs to the installation, not to any account.
/// </para>
/// </remarks>
public sealed class AccountDeletingHandler
{
    /// <summary>The Ftp module's database context.</summary>
    private readonly FtpDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Ftp module's database context.</param>
    public AccountDeletingHandler(FtpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Deletes every FTPS login row belonging to the account being removed.</summary>
    /// <remarks>
    /// The tenant query filter is deliberately bypassed: the filter governs what a REQUEST may see,
    /// and this is the removal of the account the rows belong to, authorised before the event was
    /// published. A filter here would answer correctly while account deletion stays an
    /// administrator's operation and leave rows behind silently the day it does not.
    /// </remarks>
    /// <param name="message">The account about to be deleted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task HandleAsync(AccountDeleting message, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // the account is being deleted, so its rows must be found whoever asked for the deletion
        var owned = await _dbContext.FtpUsers
            .IgnoreQueryFilters()
            .Where(row => row.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
        if (owned.Count == 0)
        {
            return;
        }

        _dbContext.FtpUsers.RemoveRange(owned);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
