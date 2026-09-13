using Maran.Modules.Sftp.Persistence;
using Maran.Sdk.Events;

namespace Maran.Modules.Sftp.IntegrationEvents.Handlers;

/// <summary>
/// Removes this module's rows for an account that is about to be deleted
/// (<see cref="AccountDeleting"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> <c>userdel</c> does not touch sshd, so nothing about deleting an
/// account removed an <c>SftpUser</c> row until this handler did. System user names are recycled,
/// so an account created again under the same name would find the previous tenant's logins listed
/// in the panel as its own — and, until the agent's own cascade existed, those logins still worked.
/// </para>
/// <para>
/// <b>This removes ROWS, not logins.</b> The host-side cleanup is the agent's, driven from the same
/// account deletion: it revokes every login the PASSWORD DATABASE holds for the account, takes the
/// jail's bind mount down and removes the jail. It reads the machine rather than replaying this
/// table, because a table can only describe what the panel remembers creating.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here propagates to the Accounts handler,
/// which abandons the deletion with the account intact — the recoverable half.
/// </para>
/// </remarks>
public sealed class AccountDeletingHandler
{
    /// <summary>The Sftp module's database context.</summary>
    private readonly SftpDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sftp module's database context.</param>
    public AccountDeletingHandler(SftpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Deletes every SFTP login row belonging to the account being removed.</summary>
    /// <remarks>
    /// The tenant query filter is deliberately bypassed, for the reason the Databases module's
    /// handler sets out: the filter governs what a REQUEST may see, and this is the removal of the
    /// account the rows belong to, authorised before the event was published. A filter here would
    /// answer correctly while account deletion stays an administrator's operation and leave rows
    /// behind silently the day it does not.
    /// </remarks>
    /// <param name="message">The account about to be deleted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task HandleAsync(AccountDeleting message, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // the account is being deleted, so its rows must be found whoever asked for the deletion
        var owned = await _dbContext.SftpUsers
            .IgnoreQueryFilters()
            .Where(row => row.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
        if (owned.Count == 0)
        {
            return;
        }

        _dbContext.SftpUsers.RemoveRange(owned);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
