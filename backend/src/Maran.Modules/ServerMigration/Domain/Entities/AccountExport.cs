namespace Maran.Modules.ServerMigration.Domain.Entities;

/// <summary>
/// One account exported for another server (issue #127): the backup it was made from, the digest the signed
/// manifest will state, and the sections every module contributed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Administrator bookkeeping, not a tenant row.</b> The account it names is held as
/// <see cref="SourceAccountId"/>, deliberately not <c>AccountId</c>: a customer never reads this table, and a
/// tenant filter on it would show an administrator nothing. The census exemption names it with this reason.
/// </para>
/// <para>
/// The export is plaintext at rest until <see cref="ExpiresAt"/> (decision D11): the backup artifact in the
/// agent's root-only directory and the sections in this row. The screen says so.
/// </para>
/// </remarks>
public sealed class AccountExport
{
    /// <summary>The longest account name stored.</summary>
    public const int AccountNameMaxLength = 32;

    /// <summary>The row's id.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account that was exported.</summary>
    public Guid SourceAccountId { get; private set; }

    /// <summary>The account's system user name when it was exported.</summary>
    public string SourceAccountName { get; private set; }

    /// <summary>The backup the artifact is read from; the Backups module owns it.</summary>
    public Guid BackupId { get; private set; }

    /// <summary>The artifact's size.</summary>
    public long SizeBytes { get; private set; }

    /// <summary>The artifact's lowercase hexadecimal SHA-256.</summary>
    public string Sha256 { get; private set; }

    /// <summary>How many database dumps the artifact holds.</summary>
    public int DatabaseCount { get; private set; }

    /// <summary>The sections every module contributed, as the JSON array the signed header carries.</summary>
    public string SectionsJson { get; private set; }

    /// <summary>The administrator who exported.</summary>
    public Guid CreatedByUserId { get; private set; }

    /// <summary>When the export was made.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the export expires and its package is no longer served.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Records an export that is ready to be downloaded or pulled.</summary>
    /// <param name="id">The row's id.</param>
    /// <param name="sourceAccountId">The account that was exported.</param>
    /// <param name="sourceAccountName">The account's system user name.</param>
    /// <param name="artifact">The backup the artifact is read from.</param>
    /// <param name="sectionsJson">The sections the modules contributed.</param>
    /// <param name="createdByUserId">The administrator who exported.</param>
    /// <param name="createdAt">When the export was made.</param>
    /// <param name="retention">How long the export stays available.</param>
    public AccountExport(
        Guid id,
        Guid sourceAccountId,
        string sourceAccountName,
        Maran.Sdk.Contracts.MigrationExportArtifact artifact,
        string sectionsJson,
        Guid createdByUserId,
        DateTimeOffset createdAt,
        TimeSpan retention)
    {
        Id = id;
        SourceAccountId = sourceAccountId;
        SourceAccountName = sourceAccountName;
        BackupId = artifact.BackupId;
        SizeBytes = artifact.SizeBytes;
        Sha256 = artifact.Sha256;
        DatabaseCount = artifact.DatabaseCount;
        SectionsJson = sectionsJson;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
        ExpiresAt = createdAt + retention;
    }

    /// <summary>For EF Core only.</summary>
    private AccountExport()
    {
        SourceAccountName = string.Empty;
        Sha256 = string.Empty;
        SectionsJson = string.Empty;
    }

    /// <summary>Whether the export can still be served.</summary>
    /// <param name="now">The current time.</param>
    /// <returns><c>true</c> until <see cref="ExpiresAt"/>.</returns>
    public bool IsAvailable(DateTimeOffset now)
    {
        return now < ExpiresAt;
    }
}
