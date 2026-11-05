using Maran.Modules.Sftp.Domain.Policies;
using Maran.Modules.Sftp.Domain.ValueObjects;
using Maran.Modules.Sftp.Resources;

namespace Maran.Modules.Sftp.Domain.Entities;

/// <summary>
/// One SFTP login created for an account: a real system account on the host, jailed into that
/// account's own bind-mount chroot (spec §11). This row — not <c>/etc/passwd</c> — is the record of
/// who owns what.
/// </summary>
/// <remarks>
/// <para>
/// <b>This entity is the tenant boundary of the whole module.</b> The host's user namespace is
/// global and the host has no notion of a tenant, so a login name only looks like it belongs to an
/// account because of the prefix the panel put there. Deciding ownership from those names is a
/// prefix scan, and a prefix scan aliases account <c>alice</c> onto <c>alice_bob</c>'s logins —
/// <c>alice_bob_deploy</c> starts with <c>alice_</c>. Every listing, read, delete and password reset
/// in this module is therefore authorised by these rows through the context's tenant query filter,
/// exactly as a database or a site is.
/// </para>
/// <para>
/// <b>There is no password column, of any kind — not plaintext, not encrypted, not a hash.</b> The
/// panel mints a password, shows it once and forgets it; the host's own shadow entry is the only
/// copy in the system. A stored copy, however it is encrypted, is a copy that can be read out of the
/// panel's database, and a panel that can read back every customer's SFTP password is a single theft
/// away from every customer's files. A customer who lost theirs gets a new one set
/// (<c>ResetSftpUserPassword</c>), never the old one shown again.
/// </para>
/// <para>
/// <b>There is no chroot path and no protocol column either, and both absences are the design.</b>
/// OpenSSH confines every login in this module with a fixed <c>ChrootDirectory %h</c>, and the agent
/// derives <c>%h</c> — the account's root-owned jail, with the account's real home bind-mounted
/// inside it — from the validated account name alone. So the customer supplies no path: there is
/// nothing here to validate, nothing to store, and the entire chroot-escape class of bug has nothing
/// to aim at. A protocol column would be equally empty: this panel serves file transfer over
/// OpenSSH's <c>internal-sftp</c> and installs no FTP daemon at all, so the column could only ever
/// hold one value.
/// </para>
/// <para>
/// Nothing here has a public setter and there is no method that changes any of it, because nothing
/// about a login changes after it is made: its home, jail, shell and group are all derived from the
/// account rather than chosen, renaming a system user is a delete and a create, and the password —
/// the one thing that does change — is deliberately not here.
/// </para>
/// <para>
/// The one thing that does belong to a login and change is its set of public keys
/// (<see cref="Keys"/>). They are public material, so holding them in the panel is not the
/// password problem above; the panel is the source of truth for the set and sends the agent the
/// whole of it whenever it changes. The set is only complete in memory when the caller loaded
/// <see cref="Keys"/> (an <c>Include</c>), under the per-login row lock the key handlers take, so
/// <see cref="AddKey"/> and <see cref="RemoveKey"/> decide against the full set.
/// </para>
/// </remarks>
public sealed class SftpUser
{
    /// <summary>The public keys registered for this login; EF Core reads and writes this field.</summary>
    private readonly List<SftpUserKey> _keys = [];

    /// <summary>The row's identity, and the only identifier a customer's request may name.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account that owns this login. Every tenant-scoped query is closed over this column.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The name the customer asked for, without the account prefix.</summary>
    /// <remarks>
    /// What the customer typed and what a screen shows them, so a customer never has to know the
    /// prefix exists. Unique within the account and NOT across the host: the prefix is what makes
    /// <c>deploy</c> available to every account at once.
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

    /// <summary>
    /// The public keys registered for this login. Empty unless the caller loaded them; a decision
    /// about the set is only valid against a loaded one.
    /// </summary>
    public IReadOnlyCollection<SftpUserKey> Keys
    {
        get
        {
            return _keys;
        }
    }

    /// <summary>Records an SFTP login the agent has already created on the host.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="accountId">The account that owns this login.</param>
    /// <param name="name">The name the customer asked for, without the account prefix.</param>
    /// <param name="fullName">The system login the host holds, as the agent reported it.</param>
    /// <param name="createdAt">The creation instant, taken from <see cref="IClock"/>.</param>
    public SftpUser(Guid id, Guid accountId, string name, string fullName, DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        Name = name;
        FullName = fullName;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private SftpUser()
    {
        Name = string.Empty;
        FullName = string.Empty;
    }

    /// <summary>Registers a public key for this login.</summary>
    /// <param name="keyId">The new row's identity.</param>
    /// <param name="label">The customer's name for the key; trimmed, 1 to 64 characters, no control character.</param>
    /// <param name="publicKey">The parsed key; its fingerprint must not already be on this login.</param>
    /// <param name="createdAt">The registration instant, taken from <see cref="IClock"/>.</param>
    /// <returns>
    /// The new key, or <c>SftpKeyLabelInvalid</c> (validation) for a bad label, <c>SftpKeyDuplicate</c>
    /// (conflict) when the fingerprint is already held, or <c>SftpKeyLimitReached</c> (conflict) when
    /// the login already holds <see cref="SftpKeyLimits.MaxPerLogin"/> keys.
    /// </returns>
    public Result<SftpUserKey> AddKey(Guid keyId, string label, SftpPublicKey publicKey, DateTimeOffset createdAt)
    {
        var trimmed = label.Trim();
        if (trimmed.Length == 0 || trimmed.Length > SftpKeyLimits.MaxLabelLength || trimmed.Any(char.IsControl))
        {
            return Result<SftpUserKey>.Fail(
                Error.Of(nameof(ErrorMessages.SftpKeyLabelInvalid), ErrorType.Validation));
        }

        if (_keys.Any(key =>
        {
            return string.Equals(key.Fingerprint, publicKey.Fingerprint, StringComparison.Ordinal);
        }))
        {
            return Result<SftpUserKey>.Fail(Error.Of(nameof(ErrorMessages.SftpKeyDuplicate), ErrorType.Conflict));
        }

        if (_keys.Count >= SftpKeyLimits.MaxPerLogin)
        {
            return Result<SftpUserKey>.Fail(Error.Of(nameof(ErrorMessages.SftpKeyLimitReached), ErrorType.Conflict));
        }

        var added = new SftpUserKey(keyId, this, trimmed, publicKey, createdAt);
        _keys.Add(added);

        return Result<SftpUserKey>.Ok(added);
    }

    /// <summary>Removes one of this login's keys.</summary>
    /// <param name="keyId">The key's identity; a key of another login is simply not found here.</param>
    /// <returns>
    /// The removed key (so the caller can journal its label and fingerprint), or
    /// <c>SftpKeyNotFound</c> (not found) when this login holds no such key. The removed row is
    /// deleted on save because its relationship to the login is required.
    /// </returns>
    public Result<SftpUserKey> RemoveKey(Guid keyId)
    {
        var key = _keys.Find(candidate =>
        {
            return candidate.Id == keyId;
        });
        if (key is null)
        {
            return Result<SftpUserKey>.Fail(Error.Of(nameof(ErrorMessages.SftpKeyNotFound), ErrorType.NotFound));
        }

        _keys.Remove(key);

        return Result<SftpUserKey>.Ok(key);
    }
}
