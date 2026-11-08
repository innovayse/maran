namespace Maran.Modules.Backups.Domain.Entities;

/// <summary>
/// The recorded instruction that an account is left out of the unattended backup schedule: one row per
/// opted-out account, and no row for every account that is backed up as usual.
/// </summary>
/// <remarks>
/// <para>
/// <b>The row IS the exclusion, and its absence is the default.</b> An account created without the
/// choice is backed up by every schedule that covers it, so the common case writes nothing and a missing
/// row can only ever mean "back it up". The opposite arrangement — a row per account saying "yes" —
/// would make the failure of this module's handler (no row written) silently stop an account being
/// backed up, which is the dangerous direction; as it is, a lost row backs an account up that nobody
/// wanted backed up, which costs disk and loses nothing.
/// </para>
/// <para>
/// <b>It excludes the account from the SCHEDULE only.</b> A backup the account's owner or an
/// administrator asks for by hand, and the safety copies taken before a restore or a deletion, are
/// unaffected: the choice was made at creation about the nightly sweep, not about whether the data may
/// ever be copied. There is no operation that removes the row yet (a follow-up: nothing in the panel
/// flips the choice later), so a reader will not find a method for it and must not add one without a use
/// case calling it.
/// </para>
/// <para>
/// <b>The account id is the key.</b> At most one row per account, which also makes the writer idempotent:
/// a second delivery of the creation event collides on the key instead of producing a second row.
/// </para>
/// </remarks>
public sealed class AccountBackupOptOut
{
    /// <summary>The account that is left out of scheduled backups; also the row's identity.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The instant the choice was recorded, taken from <see cref="IClock"/>.</summary>
    public DateTimeOffset OptedOutAt { get; private set; }

    /// <summary>Records that an account is left out of scheduled backups.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="optedOutAt">The instant the choice was recorded, taken from <see cref="IClock"/>.</param>
    public AccountBackupOptOut(Guid accountId, DateTimeOffset optedOutAt)
    {
        AccountId = accountId;
        OptedOutAt = optedOutAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private AccountBackupOptOut()
    {
    }
}
