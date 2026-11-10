using Maran.Modules.Backups.Domain.Entities;

namespace Maran.Modules.Backups.Domain.Policies;

/// <summary>
/// How many backups one account may hold that nothing else bounds: the ones a customer makes by hand
/// and the safety copies a restore leaves behind (issues #478 and #479).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a count exists at all.</b> Every backup is a full copy of an account's home and databases,
/// written into the host's shared backup root, outside the customer's own disk quota. The only other
/// brake is the agent's host-wide free-space floor, and reaching that floor refuses writes for every
/// tenant. A customer who can create backups without limit can therefore take the host down for the
/// others from an ordinary login. The schedule's retain figure bounds the SCHEDULED backups; nothing
/// bounded these two kinds.
/// </para>
/// <para>
/// <b>One number for the customer's own copies, one for the by-product.</b> Manual and pre-restore
/// backups share <see cref="MaximumCustomerBackups"/>, so a restore's safety copy takes room a manual
/// backup could have had instead of adding to it. The pre-restore copies are additionally held to
/// <see cref="MaximumPreRestoreCopies"/> by an unattended pass, so restoring repeatedly cannot crowd
/// every manual backup out of that shared number: at most three of the ten are ever safety copies.
/// </para>
/// <para>
/// <b>A manual backup past the limit is refused, never silently made room for.</b> A manual backup is
/// the customer's deliberate copy; deleting the oldest to fit a new one would destroy data they chose
/// to keep. A pre-restore copy is the opposite: nobody chose it, and it is the one the pass prunes.
/// </para>
/// <para>
/// <b>The check is not atomic.</b> It counts, then the agent writes; two requests racing can both pass
/// at nine. The agent serialises backups per account, so the overshoot is bounded by the requests in
/// flight at once and the next creation is refused. That is stated here so it is not mistaken for a
/// hard quota.
/// </para>
/// </remarks>
public static class AccountBackupAllowance
{
    /// <summary>The most manual plus pre-restore backups one account may hold.</summary>
    public const int MaximumCustomerBackups = 10;

    /// <summary>The most pre-restore copies kept per account; older ones are pruned.</summary>
    public const int MaximumPreRestoreCopies = 3;

    /// <summary>Whether the account may make one more backup by hand.</summary>
    /// <param name="accountBackups">Every backup row of the account.</param>
    /// <returns><c>true</c> while the rows that count against the allowance number fewer than the maximum.</returns>
    public static bool HasRoomForManualBackup(IEnumerable<Backup> accountBackups)
    {
        ArgumentNullException.ThrowIfNull(accountBackups);

        return accountBackups.Count(backup => { return backup.CountsAgainstAllowance(); }) < MaximumCustomerBackups;
    }
}
