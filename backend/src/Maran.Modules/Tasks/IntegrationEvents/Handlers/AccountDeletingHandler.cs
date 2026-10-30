using Maran.Modules.Tasks.Persistence;
using Maran.Sdk.Events;

namespace Maran.Modules.Tasks.IntegrationEvents.Handlers;

/// <summary>
/// Releases this module's task rows from an account that is about to be deleted
/// (<see cref="AccountDeleting"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Released, not removed.</b> A task is the operator's record of what the panel did — a backup
/// taken, a certificate issued — and that history is not the customer's to take with them. So the
/// rows stay and only their account is forgotten (<c>PanelTask.ReleaseAccount</c>): from then on no
/// customer's account matches them, so only an administrator reads them.
/// </para>
/// <para>
/// <b>Why it must happen at all.</b> A row still naming the account is residue, and the residue
/// audit refuses a deletion that leaves any. It would also hand the task to whoever is later given
/// an account under the same id.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here propagates to the Accounts handler,
/// which abandons the deletion with the account intact — the recoverable half.
/// </para>
/// </remarks>
public sealed class AccountDeletingHandler
{
    /// <summary>The Tasks module's database context.</summary>
    private readonly TasksDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Tasks module's database context.</param>
    public AccountDeletingHandler(TasksDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Detaches every task recorded against the account being removed from that account.</summary>
    /// <remarks>
    /// The tenant query filter is deliberately bypassed: the filter governs what a REQUEST may see,
    /// and this is the removal of the account the rows belong to, authorised before the event was
    /// published. A filter here would answer correctly while account deletion stays an
    /// administrator's operation and leave rows behind silently the day it does not.
    /// </remarks>
    /// <param name="message">The account about to be deleted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Resolves once the rows are released.</returns>
    public async Task HandleAsync(AccountDeleting message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

#pragma warning disable RS0030 // the account is being deleted, so its tasks must be found whoever asked for the deletion
        var owned = await _dbContext.PanelTasks
            .IgnoreQueryFilters()
            .Where(row => row.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
        if (owned.Count == 0)
        {
            return;
        }

        foreach (var task in owned)
        {
            task.ReleaseAccount();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
