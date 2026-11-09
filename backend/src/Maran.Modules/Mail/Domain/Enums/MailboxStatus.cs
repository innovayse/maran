namespace Maran.Modules.Mail.Domain.Enums;

/// <summary>
/// How a mailbox reads to its owner right now: working, suspended with its account, or switched off.
/// A reading, not a stored column: the row stores only whether its owner switched it off, and the
/// account's suspension is read from the account when the mailbox is shown.
/// </summary>
public enum MailboxStatus
{
    /// <summary>The mailbox works: its owner can sign in and mail is delivered to it.</summary>
    Active,

    /// <summary>The mailbox's account is suspended, so the host refuses its logins and defers its mail.</summary>
    Suspended,

    /// <summary>
    /// The mailbox is switched off by its owner or an administrator: no login, no new mail accepted for
    /// it, and what it holds is kept.
    /// </summary>
    Disabled,
}
