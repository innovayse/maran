using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Persistence;
using Maran.Sdk.Events;

namespace Maran.Modules.Backups.IntegrationEvents.Handlers;

/// <summary>
/// Removes this module's rows for an account that is about to be deleted
/// (<see cref="AccountDeleting"/>) — every one of them except the final backup taken for that very
/// deletion.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it must exist.</b> A backup row names an account. Left behind, it is a customer's backup
/// listed in a panel that no longer has an account for it — and, worse than the site rows that made
/// this a mechanical check, it is a restore button pointing at an archive of a customer who is gone.
/// <c>AccountCascadeTests</c> fails any module whose model carries an <c>AccountId</c> and handles
/// no <see cref="AccountDeleting"/>, and the runtime residue auditor refuses the deletion outright
/// if a row still names the account — so this handler is what makes an account deletable at all
/// once this module is composed.
/// </para>
/// <para>
/// <b>This removes ROWS, not artifacts, and that is a decision rather than an oversight.</b> The
/// archives on the destination are root-owned files outside every home, and they are the last copy
/// of a customer's data in existence at the moment their account is destroyed. Deleting them from
/// inside a cascade would mean the panel's most destructive operation silently taking the only thing
/// that could undo it, in the same breath, with no separate confirmation — and an operator who
/// deleted an account by mistake would have nothing left. The artifacts remain for the operator to
/// remove deliberately, which is a disk-space matter they can see, not a correctness one.
/// </para>
/// <para>
/// <b>The consequence is stated so nobody discovers it as a surprise:</b> after a deletion, the
/// account's ordinary archives are on the destination with no row naming them, and the panel will
/// never mention them again. It is the honest half of the trade above, and the operator's own
/// retention of that directory is what bounds it. The one archive that keeps its row is the final
/// backup below, which is the point of keeping it: the copy that matters stays findable.
/// </para>
/// <para>
/// <b>One row is deliberately kept: the final backup taken for THIS deletion.</b>
/// <see cref="Backup.SurvivesAccountDeletion"/> is the rule, and it lives on the entity because the
/// Host's residue audit has to apply the same one — a deletion is refused while anything still names
/// the account, so an exemption spelled twice would either refuse every deletion this module
/// protects or quietly widen. The kept row's <c>AccountId</c> then names an account that is gone,
/// which is intended: an administrator can still see it, no customer can, and it is the only copy of
/// that customer's data left in the world.
/// </para>
/// <para>
/// <b>The kept row is STAMPED with the account's user name, and that is what makes it releasable
/// afterwards.</b> An archive is addressed by the account's system user name, and every path that
/// deletes one used to read that name from the accounts directory — which answers "no such account"
/// for this row for ever. So the row could not be deleted at all: it and its archive accumulated,
/// one per deleted account, with no code anywhere that could remove either. The stamp closes that,
/// and this is the last moment it can be taken, because <see cref="AccountDeleting.Username"/> is
/// the final place the name exists.
/// </para>
/// <para>
/// <b>What bounds these rows is an administrator, deliberately, and not a timer.</b> They are
/// exempt from retention by <see cref="Backup.MayBeRetentionPruned"/> and always will be: a pass
/// that expired them would destroy the last copy of a departed customer's data on a schedule
/// nobody watched, which is a worse failure than a table that grows by one row per deleted account.
/// An administrator sees them in the ordinary backups list (the tenant filter admits every row for
/// an administrator, and matches no customer for these, because the account they name is gone) and
/// releases one with the ordinary deletion, which now removes the archive and the row together.
/// </para>
/// <para>
/// <b>This module's SCHEDULES are removed outright.</b> An account's own backup schedule is an
/// instruction to back up something that no longer exists; keeping it would leave the nightly sweep
/// selecting a row whose target it can never find, and the residue audit would refuse the deletion
/// over it.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here propagates to the Accounts handler,
/// which abandons the deletion with the account intact — the recoverable half.
/// </para>
/// </remarks>
public sealed class AccountDeletingHandler
{
    /// <summary>The Backups module's database context.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    public AccountDeletingHandler(BackupsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Deletes the account's backup rows and its backup schedule, keeping — and stamping — the rows
    /// <see cref="Backup.SurvivesAccountDeletion"/> names.
    /// </summary>
    /// <remarks>
    /// The tenant query filter is deliberately bypassed, for the reason the other modules' handlers
    /// set out: the filter governs what a REQUEST may see, and this is the removal of the account
    /// the rows belong to, authorised before the event was published.
    /// </remarks>
    /// <param name="message">The account about to be deleted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task HandleAsync(AccountDeleting message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

#pragma warning disable RS0030 // the account is being deleted, so its rows must be found whoever asked for the deletion
        var owned = await _dbContext.Backups
            .IgnoreQueryFilters()
            .Where(row => row.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030

        // The final backup is kept, and it is the entity that says which one that is — the same
        // predicate the Host's residue audit applies, so the exemption cannot widen on one side
        // only. Filtered in memory rather than in the query because the rule belongs to the entity
        // and a translated `Kind != PreDeletion` would be a second statement of it in SQL.
        var removed = owned.Where(row => { return !row.SurvivesAccountDeletion(); }).ToList();

        // Stamped before the removal is saved, so a row that is kept always leaves this handler
        // able to name the account it belongs to. Without the name its archive is unaddressable and
        // the row is undeletable — which is the state every PreDeletion backup was in.
        foreach (var kept in owned.Where(row => { return row.SurvivesAccountDeletion(); }))
        {
            kept.Orphan(message.Username);
        }

#pragma warning disable RS0030 // the account is being deleted, so its rows must be found whoever asked for the deletion
        var schedules = await _dbContext.BackupSchedules
            .IgnoreQueryFilters()
            .Where(row => row.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030

        _dbContext.Backups.RemoveRange(removed);
        _dbContext.BackupSchedules.RemoveRange(schedules);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
