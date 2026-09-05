using Maran.Modules.Databases.Persistence;
using Maran.Sdk.Events;

namespace Maran.Modules.Databases.IntegrationEvents.Handlers;

/// <summary>
/// Removes this module's rows for an account that is about to be deleted
/// (<see cref="AccountDeleting"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> <c>userdel</c> does not touch MySQL, so nothing about deleting an
/// account removed a <c>Database</c> row until this handler did. What was left behind was worse
/// than clutter: system user names are recycled, so an account created again under the same name
/// would find the previous tenant's rows in the panel, pointing at the previous tenant's data.
/// </para>
/// <para>
/// <b>This removes ROWS, not databases.</b> The server-side cleanup is the agent's, driven from the
/// same account deletion and asking MySQL what it actually holds rather than replaying this table —
/// a list can only describe what the panel remembers creating, and an account deletion has to
/// remove what it has forgotten too. Neither half is a substitute for the other, and the ordering
/// makes that safe: the Accounts handler publishes this event before it calls the agent, so a row
/// removed here is always followed by a host cleanup that finds the database by name regardless.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here propagates to the Accounts handler,
/// which abandons the deletion with the account intact. That is the recoverable half: an account
/// that is still there can be deleted again, whereas a row silently left behind is only discovered
/// by the customer who inherits it.
/// </para>
/// </remarks>
public sealed class AccountDeletingHandler
{
    /// <summary>The Databases module's database context.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Databases module's database context.</param>
    public AccountDeletingHandler(DatabasesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Deletes every database row belonging to the account being removed.</summary>
    /// <remarks>
    /// The tenant query filter is deliberately bypassed. It is an authorisation control over what a
    /// REQUEST may see, and this is not a request for rows — it is the removal of the account those
    /// rows belong to, already authorised by the Accounts module before the event was published. A
    /// filtered query here would answer correctly today, because account deletion is an
    /// administrator's operation and an administrator sees everything, and would silently leave a
    /// customer's rows behind the day that changed. Silently is the whole problem.
    /// </remarks>
    /// <param name="message">The account about to be deleted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task HandleAsync(AccountDeleting message, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // the account is being deleted, so its rows must be found whoever asked for the deletion
        var owned = await _dbContext.Databases
            .IgnoreQueryFilters()
            .Where(row => row.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
        if (owned.Count == 0)
        {
            return;
        }

        _dbContext.Databases.RemoveRange(owned);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
