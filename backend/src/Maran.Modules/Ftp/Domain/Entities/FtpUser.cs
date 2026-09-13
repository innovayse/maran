namespace Maran.Modules.Ftp.Domain.Entities;

/// <summary>
/// One FTPS login created for an account: a real system account on the host, jailed into that
/// account's own FTPS jail with the account's real home bind-mounted inside it (spec §11). This row
/// — not <c>/etc/passwd</c> — is the record of who owns what.
/// </summary>
/// <remarks>
/// <para>
/// <b>This entity is the tenant boundary of every customer-facing path in this module.</b> The
/// host's user namespace is global and the host has no notion of a tenant, so a login name only
/// looks like it belongs to an account because of the prefix the panel put there. Deciding ownership
/// from those names is a prefix scan, and a prefix scan aliases account <c>alice</c> onto
/// <c>alice_bob</c>'s logins — <c>alice_bob_deploy</c> starts with <c>alice_</c>. Every listing,
/// read, delete and password reset is therefore authorised by these rows through the context's
/// tenant query filter. The agent holds the same line one layer down and by a different mechanism:
/// it matches the candidate's passwd home against the account's jail rather than decomposing its
/// name (<c>ops/src/ftps/delete_ftps_user.rs</c>).
/// </para>
/// <para>
/// <b>There is no password column, of any kind — not plaintext, not encrypted, not a hash.</b> The
/// panel mints a password, shows it once and forgets it; the host's own shadow entry is the only
/// copy in the system. A stored copy, however it is encrypted, is a copy that can be read out of the
/// panel's database, and a panel that can read back every customer's FTPS password is a single theft
/// away from every customer's files. A customer who lost theirs gets a new one set
/// (<c>ResetFtpUserPassword</c>), never the old one shown again. That absence is asserted by a
/// reflection test rather than left to a reviewer noticing a new property one day, because it is the
/// axis on which this design silently stops being true.
/// </para>
/// <para>
/// <b>There is no jail path here and there is nothing for one to come from.</b> The agent derives
/// the jail from the validated account name alone and creates it root-owned, so the customer names
/// no directory: there is nothing to validate, nothing to store, and the chroot-escape class of bug
/// has nothing to aim at.
/// </para>
/// <para>
/// <b>There is no protocol column either, and its absence is the opposite of the DTO's presence.</b>
/// Every row in this table is an FTPS login by construction — the module owns one daemon — so a
/// column could hold only one value. <c>FtpUserDto</c> nevertheless carries the label, because the
/// SPA merges this module's logins with the Sftp module's on one screen and must render a fact the
/// backend produced rather than one derived from which URL it happened to call.
/// </para>
/// <para>
/// Nothing here has a public setter and there is no method that changes any of it, because nothing
/// about a login changes after it is made: its home, jail, shell and group are all derived from the
/// account rather than chosen, renaming a system user is a delete and a create, and the password —
/// the one thing that does change — is deliberately not here.
/// </para>
/// </remarks>
public sealed class FtpUser
{
    /// <summary>The row's identity, and the only identifier a customer's request may name.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account that owns this login. Every tenant-scoped query is closed over this column.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The name the customer asked for, without the account prefix.</summary>
    /// <remarks>
    /// What the customer typed and what a screen shows them, so a customer never has to know the
    /// prefix exists. Unique within the account and NOT across the host: the prefix is what makes
    /// <c>files</c> available to every account at once.
    /// </remarks>
    public string Name { get; private set; }

    /// <summary>The system login as the host actually holds it, as the agent reported it.</summary>
    /// <remarks>
    /// Recorded rather than rebuilt from <see cref="Name"/> and the account's user name. Rebuilding
    /// would make this row's truth depend on the panel and the agent agreeing about a separator
    /// forever, and the day they disagreed the panel would show a customer a login name they cannot
    /// sign in with.
    /// </remarks>
    public string FullName { get; private set; }

    /// <summary>The instant the login was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records an FTPS login the agent has already created on the host.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="accountId">The account that owns this login.</param>
    /// <param name="name">The name the customer asked for, without the account prefix.</param>
    /// <param name="fullName">The system login the host holds, as the agent reported it.</param>
    /// <param name="createdAt">The creation instant, taken from <see cref="IClock"/>.</param>
    public FtpUser(Guid id, Guid accountId, string name, string fullName, DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        Name = name;
        FullName = fullName;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private FtpUser()
    {
        Name = string.Empty;
        FullName = string.Empty;
    }
}
