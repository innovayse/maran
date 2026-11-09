using Maran.Modules.Backups.Domain.Enums;

namespace Maran.Modules.Backups.Domain.Entities;

/// <summary>
/// A place the panel records backups as living: today the one local root the agent writes to, and
/// the type a remote destination will be described by (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// <b>Host-wide, not tenant-owned: there is no <c>AccountId</c> here.</b> Where the server keeps its
/// archives is a decision about the machine and its disks, and a customer has no business naming
/// one. That also means the row is invisible to <c>TenantScopeTests</c>, which walks only entities
/// carrying an account id — so this entity needs no exemption there, and adding one would fail that
/// suite's own staleness guard.
/// </para>
/// <para>
/// <b><see cref="Path"/> holds nothing for a local destination, and the reason is that the panel has
/// no statement of that path to hold.</b> Measured rather than assumed: the agent's
/// <c>validated_destination</c> refuses a local destination that carries a path at all — "a local
/// destination carries no path: the agent's backup root is its own" — and writes under its own
/// constant <c>AgentPaths::BACKUP_ROOT</c>. Until 2026-09-08 this column carried a copy of a panel
/// setting, <c>Backups__LocalRoot</c>, which the docs here called a second statement of the same
/// literal; nothing made that true, so an operator who set the setting to <c>/srv/backups</c> got a
/// screen and a row saying <c>/srv/backups</c> and archives written to <c>/var/backups/maran</c> —
/// a wrong directory read at the one moment it is read, a recovery. The setting is gone and the
/// directory is now READ from the agent's handshake (<c>AgentInfo.backup_root</c>) at the moment a
/// screen shows it, so there is one statement of it and the panel can show nothing else. This
/// column keeps its historic values until a contract-phase migration removes it
/// (rules/architecture.md "expand, then contract"); nothing reads it for a local destination.
/// </para>
/// <para>
/// <b>Exactly one local destination can exist, and that is the agent's contract rather than a
/// policy.</b> The agent takes no root, so a second local row would be a row the panel could not
/// honour — the same class of untruth as a foreign key to a table that is not there. A second local
/// root becomes possible on the day the agent accepts one, and not before.
/// </para>
/// <para>
/// <b>What is deliberately absent: the S3 credential columns.</b> No path can store a remote
/// destination on this build, so a column holding an encrypted secret would be a promise whose only
/// evidence is a column. The remote arm brings its own columns when it brings the code that writes
/// them.
/// </para>
/// </remarks>
public sealed class BackupDestination
{
    /// <summary>The identity of the destination the seeder reconciles, fixed for the life of a server.</summary>
    /// <remarks>
    /// Fixed rather than minted, so the seeder can be a reconciliation of one known row instead of a
    /// query for "is the list empty" — a shape this repository has already shipped once and recorded
    /// as a defect, because deleting the seeded row made the next restart restore it.
    /// </remarks>
    public static readonly Guid DefaultDestinationId = Guid.Parse("33333333-0000-4000-8000-000000000001");

    /// <summary>The longest operator-supplied label this table stores.</summary>
    public const int NameMaxLength = 64;

    /// <summary>The longest path this table stores, matching the kernel's own ceiling.</summary>
    public const int PathMaxLength = 4096;

    /// <summary>The destination's identity, which is what a backup row and a schedule point at.</summary>
    public Guid Id { get; private set; }

    /// <summary>The label an operator reads on a screen.</summary>
    public string Name { get; private set; }

    /// <summary>Which kind of storage this names.</summary>
    public BackupDestinationKind Kind { get; private set; }

    /// <summary>Empty for a local destination, whose directory only the agent can state.</summary>
    /// <remarks>
    /// Retained for the remote arm, which will store a prefix it genuinely owns, and because the
    /// last release still reads this column. It is NOT the source of what a screen shows for a
    /// local destination — see the type's remarks.
    /// </remarks>
    public string Path { get; private set; }

    /// <summary>Whether this is the destination a backup that names none is written to.</summary>
    /// <remarks>
    /// Exactly one row carries it, enforced by a filtered unique index. It is what gives
    /// <c>Backup.DestinationId</c>'s historic nulls a referent: a null was written when no
    /// destination table existed and meant "the configured local root", which is this row.
    /// </remarks>
    public bool IsDefault { get; private set; }

    /// <summary>When the panel first recorded this destination.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Whether NEW backups written to this destination are sealed (encrypted) by the agent.</summary>
    /// <remarks>
    /// <para>
    /// A policy about the future and nothing else. It says what the next backup does; it says nothing
    /// about backups already written, which are sealed or not according to what was true the day they
    /// were taken and which the agent recognises by the file's own first bytes. Switching it off
    /// therefore never strands a sealed backup: <see cref="ArchiveKey"/> stays, and every restore,
    /// verification and safety copy reads with it.
    /// </para>
    /// <para>
    /// <b>True implies <see cref="ArchiveKey"/> is not <c>null</c></b>, and only
    /// <see cref="EnableArchiveEncryption"/> sets it, which is what makes "encrypt with a key that
    /// does not exist" unrepresentable.
    /// </para>
    /// </remarks>
    public bool EncryptArchives { get; private set; }

    /// <summary>
    /// The key this destination's sealed archives were sealed with, or <c>null</c> when encryption has
    /// never been switched on here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Plain text in the domain, ciphertext in the database.</b> The column goes through
    /// <c>EncryptedStringConverter</c> (the panel's <c>IEncryptionService</c>, whose key is in
    /// <c>panel.env</c>), exactly as the TOTP secret and the SMTP password do (rules/security.md
    /// item 8). It is NEVER copied onto a DTO, a log line, an audit entry, a personal-data export or
    /// an exception: <c>BackupDestinationDto</c> carries <see cref="EncryptArchives"/> and no key, and
    /// the one reader is <c>BackupDestinationMapper</c>, which hands it to the agent.
    /// </para>
    /// <para>
    /// <b>One key per destination for the life of the row, and it is never replaced.</b> Rotating it
    /// would need a key identity on every backup so that restore knew which one to send; no use case
    /// calls for that yet (rules/csharp.md: no method no use case calls), and a rotation that
    /// overwrote this value would silently strand every backup sealed under the old one.
    /// Losing this value, or the panel key that decrypts it, makes every sealed backup of the
    /// destination permanently unreadable — which is why the panel's own recovery archive carries
    /// both (docs/backups/encryption.md).
    /// </para>
    /// </remarks>
    public string? ArchiveKey { get; private set; }

    /// <summary>Opens a destination.</summary>
    /// <param name="id">The destination's identity.</param>
    /// <param name="name">The label an operator reads.</param>
    /// <param name="kind">Which kind of storage it names.</param>
    /// <param name="path">Where a local destination's artifacts rest; empty for any other kind.</param>
    /// <param name="isDefault">Whether backups naming no destination are written here.</param>
    /// <param name="createdAt">The instant it was recorded, taken from <c>IClock</c>.</param>
    public BackupDestination(
        Guid id,
        string name,
        BackupDestinationKind kind,
        string path,
        bool isDefault,
        DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Path = path;
        IsDefault = isDefault;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private BackupDestination()
    {
        Name = string.Empty;
        Path = string.Empty;
    }

    /// <summary>Switches encryption of new backups on, minting nothing: the caller supplies the key.</summary>
    /// <param name="newArchiveKey">
    /// A freshly generated 256-bit key as 64 lowercase hexadecimal characters. Used only when this
    /// destination has never had a key; an existing key is kept, so switching encryption off and on
    /// again does not strand the backups sealed in between.
    /// </param>
    /// <remarks>
    /// Idempotent: calling it on a destination that already encrypts changes nothing. It does not
    /// validate the key's shape — the generator that makes it is the only caller and the agent
    /// refuses anything but a whole key — and it refuses an empty one, because a key of nothing would
    /// turn on an encryption the agent reads as "plain".
    /// </remarks>
    public void EnableArchiveEncryption(string newArchiveKey)
    {
        if (ArchiveKey is null)
        {
            if (string.IsNullOrEmpty(newArchiveKey))
            {
                return;
            }

            ArchiveKey = newArchiveKey;
        }

        EncryptArchives = true;
    }

    /// <summary>Switches encryption of new backups off, keeping the key that opens the old ones.</summary>
    /// <remarks>
    /// The key is deliberately NOT cleared: a backup sealed while encryption was on is opened with it,
    /// and clearing it here would turn a policy change into the loss of those backups.
    /// </remarks>
    public void DisableArchiveEncryption()
    {
        EncryptArchives = false;
    }

    /// <summary>Drops any path this destination once recorded.</summary>
    /// <remarks>
    /// It exists for exactly one caller: the startup reconciliation, which clears the copy of
    /// <c>Backups__LocalRoot</c> that servers booted before 2026-09-08 still carry. Leaving it would
    /// leave a directory in the database that nobody established and that an operator reading the
    /// table in <c>psql</c> would take for the truth — the same untruth in a quieter place. There is
    /// deliberately no member that SETS a local path: the panel has no such value to set it from.
    /// </remarks>
    public void ClearRecordedPath()
    {
        Path = string.Empty;
    }
}
