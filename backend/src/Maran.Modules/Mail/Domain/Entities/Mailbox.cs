using Maran.Modules.Mail.Domain.Enums;

namespace Maran.Modules.Mail.Domain.Entities;

/// <summary>
/// One mailbox of an account: an address on one of its mail domains, with the hash of the password its
/// owner signs in with, its storage quota and its send limit.
/// </summary>
/// <remarks>
/// <para>
/// <b>The password column holds a hash and only a hash</b>, in the scheme the host verifies, produced by
/// the panel's password hasher before it reaches the database and sent to the host as it is. The panel
/// never holds the password in any recoverable form, so a customer who forgot theirs is given a way to
/// set a new one, never shown the old one. That absence is the axis on which this design silently stops
/// being true, which is why a test asserts it by reflection.
/// </para>
/// <para>
/// <b>Suspension is not a column.</b> An account's suspension is applied to the host as a whole (every
/// mailbox of the account, sessions ended) and is read from the account when a mailbox is shown. The row
/// stores only whether its owner switched the mailbox off.
/// </para>
/// </remarks>
public sealed class Mailbox
{
    /// <summary>The row's identity, and the only identifier a request may name.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account that owns the mailbox. Every tenant-scoped query is closed over this column.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The domain the mailbox lives in.</summary>
    public Guid MailDomainId { get; private set; }

    /// <summary>The domain, loaded when the mailbox is read with it.</summary>
    public MailDomain MailDomain { get; private set; } = null!;

    /// <summary>The part of the address before the <c>@</c>, lower-case.</summary>
    public string LocalPart { get; private set; }

    /// <summary>The password hash, in the scheme the host verifies. Never returned by any API.</summary>
    public string PasswordHash { get; private set; }

    /// <summary>The storage ceiling, in MiB.</summary>
    public int QuotaMib { get; private set; }

    /// <summary>The most messages the mailbox may send in an hour.</summary>
    public int SendLimitPerHour { get; private set; }

    /// <summary>Whether the owner has the mailbox switched on.</summary>
    public bool Enabled { get; private set; }

    /// <summary>What happens to mail scored as spam.</summary>
    public MailSpamAction SpamAction { get; private set; }

    /// <summary>The instant the mailbox was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records a mailbox the agent has been asked to create.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="mailDomainId">The domain the mailbox lives in.</param>
    /// <param name="localPart">The local part, already normalised.</param>
    /// <param name="passwordHash">The password hash.</param>
    /// <param name="quotaMib">The storage ceiling, in MiB.</param>
    /// <param name="sendLimitPerHour">The hourly send limit.</param>
    /// <param name="createdAt">The creation instant, taken from <see cref="IClock"/>.</param>
    public Mailbox(
        Guid id,
        Guid accountId,
        Guid mailDomainId,
        string localPart,
        string passwordHash,
        int quotaMib,
        int sendLimitPerHour,
        DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        MailDomainId = mailDomainId;
        LocalPart = localPart;
        PasswordHash = passwordHash;
        QuotaMib = quotaMib;
        SendLimitPerHour = sendLimitPerHour;
        Enabled = true;
        SpamAction = MailSpamAction.FileToJunk;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private Mailbox()
    {
        LocalPart = string.Empty;
        PasswordHash = string.Empty;
    }

    /// <summary>Replaces the limits the owner may edit.</summary>
    /// <param name="quotaMib">The new storage ceiling, in MiB.</param>
    /// <param name="sendLimitPerHour">The new hourly send limit.</param>
    public void ChangeLimits(int quotaMib, int sendLimitPerHour)
    {
        QuotaMib = quotaMib;
        SendLimitPerHour = sendLimitPerHour;
    }

    /// <summary>Replaces the password hash after the owner chose a new password.</summary>
    /// <param name="passwordHash">The hash of the new password.</param>
    public void ChangePassword(string passwordHash)
    {
        PasswordHash = passwordHash;
    }

    /// <summary>Switches the mailbox off: no login, no new mail accepted, what it holds is kept.</summary>
    public void Disable()
    {
        Enabled = false;
    }

    /// <summary>Switches a disabled mailbox back on.</summary>
    public void Reactivate()
    {
        Enabled = true;
    }
}
