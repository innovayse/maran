using Maran.Modules.ServerMigration.Domain.Enums;

namespace Maran.Modules.ServerMigration.Domain.Entities;

/// <summary>
/// One account being imported from a package (issue #127): what the verified package said, the artifact the
/// agent holds for it, and how far the job got.
/// </summary>
/// <remarks>
/// <para>
/// <b>Administrator bookkeeping, not a tenant row.</b> The account it creates is held as
/// <see cref="TargetAccountId"/>, deliberately not <c>AccountId</c>, and it is null until the Accounts importer
/// has created it. The census exemption names the entity with this reason.
/// </para>
/// <para>
/// <b>Every transition is a method.</b> The state is the record of what the job did: <see cref="Start"/>,
/// <see cref="Complete"/>, <see cref="RollBack"/>, <see cref="RollbackFailed"/> and <see cref="Discard"/> are the
/// only ways it moves, and a method refuses a move the state does not allow.
/// </para>
/// <para>
/// <b>The credentials an import generates are held encrypted until claimed once</b> (decision D3): the column is
/// encrypted at rest, <see cref="ClaimIssuedSecrets"/> returns it a single time and clears it, so a database
/// password is shown to the administrator once and kept by nobody.
/// </para>
/// </remarks>
public sealed class AccountImport
{
    /// <summary>The longest account name or domain stored.</summary>
    public const int NameMaxLength = 253;

    /// <summary>The longest stage text.</summary>
    public const int StageMaxLength = 64;

    /// <summary>The longest failure code.</summary>
    public const int FailureCodeMaxLength = 128;

    /// <summary>The row's id.</summary>
    public Guid Id { get; private set; }

    /// <summary>Where the import stands.</summary>
    public AccountImportState State { get; private set; }

    /// <summary>The account's system user name on the sending server, which this server keeps (decision D1).</summary>
    public string SourceAccountName { get; private set; }

    /// <summary>The account's primary domain on the sending server.</summary>
    public string SourcePrimaryDomain { get; private set; }

    /// <summary>The host name the sending server called itself in the package.</summary>
    public string SourceHostName { get; private set; }

    /// <summary>The fingerprint of the key that signed, as the operator confirmed it.</summary>
    public string KeyFingerprint { get; private set; }

    /// <summary>The sections of the package, as the JSON array its signed header carried.</summary>
    public string SectionsJson { get; private set; }

    /// <summary>The id the artifact is published under on this host: the sending server's backup id.</summary>
    public Guid ArtifactBackupId { get; private set; }

    /// <summary>The artifact's size.</summary>
    public long ArtifactSizeBytes { get; private set; }

    /// <summary>The artifact's lowercase hexadecimal SHA-256.</summary>
    public string ArtifactSha256 { get; private set; }

    /// <summary>How many database dumps the artifact holds.</summary>
    public int DatabaseCount { get; private set; }

    /// <summary>The size of the account's home directory in the artifact.</summary>
    public long HomeBytes { get; private set; }

    /// <summary>The administrator who staged the import and whose authority the job acts under.</summary>
    public Guid CreatedByUserId { get; private set; }

    /// <summary>When the package was accepted.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the job started, or <c>null</c>.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>When the import ended, or <c>null</c>.</summary>
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>The account the job created, or <c>null</c> until it has.</summary>
    public Guid? TargetAccountId { get; private set; }

    /// <summary>The plan the administrator chose, or <c>null</c> until the import starts.</summary>
    public Guid? PlanId { get; private set; }

    /// <summary>The reseller the administrator named as owner, or <c>null</c>.</summary>
    public Guid? TargetResellerId { get; private set; }

    /// <summary>The step the job is at, for the screen.</summary>
    public string? Stage { get; private set; }

    /// <summary>The code of what stopped the import, or <c>null</c>.</summary>
    public string? FailureCode { get; private set; }

    /// <summary>The credentials the import generated as a JSON array; encrypted in the column and cleared once claimed.</summary>
    public string? IssuedSecretsJson { get; private set; }

    /// <summary>Records a package the agent accepted.</summary>
    /// <param name="id">The row's id.</param>
    /// <param name="sourceAccountName">The account's system user name on the sending server.</param>
    /// <param name="sourcePrimaryDomain">The account's primary domain on the sending server.</param>
    /// <param name="sourceHostName">The host name the sending server called itself.</param>
    /// <param name="keyFingerprint">The fingerprint of the key that signed, as the operator confirmed it.</param>
    /// <param name="sectionsJson">The sections of the package.</param>
    /// <param name="homeBytes">The size of the home directory in the artifact.</param>
    /// <param name="artifact">The artifact the agent published for it.</param>
    /// <param name="createdByUserId">The administrator who staged the import.</param>
    /// <param name="createdAt">When the package was accepted.</param>
    public AccountImport(
        Guid id,
        string sourceAccountName,
        string sourcePrimaryDomain,
        string sourceHostName,
        string keyFingerprint,
        string sectionsJson,
        long homeBytes,
        Maran.Sdk.Contracts.MigrationImportedArtifact artifact,
        Guid createdByUserId,
        DateTimeOffset createdAt)
    {
        Id = id;
        State = AccountImportState.Staged;
        SourceAccountName = sourceAccountName;
        SourcePrimaryDomain = sourcePrimaryDomain;
        SourceHostName = sourceHostName;
        KeyFingerprint = keyFingerprint;
        SectionsJson = sectionsJson;
        ArtifactBackupId = artifact.BackupId;
        ArtifactSizeBytes = artifact.SizeBytes;
        ArtifactSha256 = artifact.Sha256;
        DatabaseCount = artifact.DatabaseCount;
        HomeBytes = homeBytes;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
    }

    /// <summary>For EF Core only.</summary>
    private AccountImport()
    {
        SourceAccountName = string.Empty;
        SourcePrimaryDomain = string.Empty;
        SourceHostName = string.Empty;
        KeyFingerprint = string.Empty;
        SectionsJson = string.Empty;
        ArtifactSha256 = string.Empty;
    }

    /// <summary>Moves a staged import to running with the administrator's choices.</summary>
    /// <param name="planId">The plan the account is created against.</param>
    /// <param name="resellerId">The reseller that is to own the account, or <c>null</c>.</param>
    /// <param name="now">The current time.</param>
    /// <returns><c>true</c> when the import was staged and is now running; <c>false</c> in any other state, changing nothing.</returns>
    public bool Start(Guid planId, Guid? resellerId, DateTimeOffset now)
    {
        if (State != AccountImportState.Staged)
        {
            return false;
        }

        State = AccountImportState.Running;
        PlanId = planId;
        TargetResellerId = resellerId;
        StartedAt = now;
        Stage = null;

        return true;
    }

    /// <summary>Records the step the job is at.</summary>
    /// <param name="stage">A short machine-stable step name.</param>
    public void AtStage(string stage)
    {
        Stage = stage;
    }

    /// <summary>Records the account the job created, so a rollback deletes exactly it and nothing else.</summary>
    /// <param name="accountId">The account's id.</param>
    public void AccountCreated(Guid accountId)
    {
        TargetAccountId = accountId;
    }

    /// <summary>Holds the credentials the job generated until the administrator claims them once.</summary>
    /// <param name="json">The credentials as a JSON array.</param>
    public void HoldIssuedSecrets(string json)
    {
        IssuedSecretsJson = json;
    }

    /// <summary>Returns the held credentials a single time and clears them.</summary>
    /// <returns>The JSON array, or <c>null</c> when there are none or they were already claimed.</returns>
    public string? ClaimIssuedSecrets()
    {
        var held = IssuedSecretsJson;
        IssuedSecretsJson = null;

        return held;
    }

    /// <summary>Ends a running import that finished.</summary>
    /// <param name="now">The current time.</param>
    public void Complete(DateTimeOffset now)
    {
        State = AccountImportState.Completed;
        Stage = null;
        FinishedAt = now;
    }

    /// <summary>Ends a running import whose account was deleted again.</summary>
    /// <param name="failureCode">The code of what stopped the import.</param>
    /// <param name="now">The current time.</param>
    public void RollBack(string failureCode, DateTimeOffset now)
    {
        State = AccountImportState.RolledBack;
        FailureCode = failureCode;
        FinishedAt = now;
        IssuedSecretsJson = null;
    }

    /// <summary>Ends a running import whose account could not be deleted again; the account remains.</summary>
    /// <param name="failureCode">The code of what stopped the import.</param>
    /// <param name="now">The current time.</param>
    public void RollbackFailed(string failureCode, DateTimeOffset now)
    {
        State = AccountImportState.RolledBackIncomplete;
        FailureCode = failureCode;
        FinishedAt = now;
        IssuedSecretsJson = null;
    }

    /// <summary>Discards a staged import without starting it.</summary>
    /// <param name="now">The current time.</param>
    /// <returns><c>true</c> when it was staged; <c>false</c> in any other state, changing nothing.</returns>
    public bool Discard(DateTimeOffset now)
    {
        if (State != AccountImportState.Staged)
        {
            return false;
        }

        State = AccountImportState.Discarded;
        FinishedAt = now;

        return true;
    }
}
