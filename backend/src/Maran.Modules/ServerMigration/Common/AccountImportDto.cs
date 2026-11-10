namespace Maran.Modules.ServerMigration.Common;

/// <summary>An import of one account, as the administrator sees it.</summary>
/// <param name="Id">The import's id.</param>
/// <param name="State">The machine-stable state: <c>Staged</c>, <c>Running</c>, <c>Completed</c>, <c>RolledBack</c>, <c>RolledBackIncomplete</c> or <c>Discarded</c>.</param>
/// <param name="StateName">The state as an operator reads it, in the caller's language.</param>
/// <param name="AccountName">The account's system user name, which this server keeps.</param>
/// <param name="PrimaryDomain">The account's primary domain on the sending server.</param>
/// <param name="SourceHostName">The name the sending server gave itself.</param>
/// <param name="KeyFingerprint">The fingerprint of the key that signed, as confirmed.</param>
/// <param name="SizeBytes">The artifact's size.</param>
/// <param name="HomeBytes">The size of the home directory in the artifact.</param>
/// <param name="DatabaseCount">How many database dumps the artifact holds.</param>
/// <param name="SectionModules">The ids of the modules whose sections the package carries.</param>
/// <param name="TargetAccountId">The account the import created, or <c>null</c> until it has.</param>
/// <param name="Stage">The step the job is at, or <c>null</c>.</param>
/// <param name="FailureCode">The code of what stopped the import, or <c>null</c>.</param>
/// <param name="FailureName">The sentence for <paramref name="FailureCode"/> in the caller's language, or <c>null</c> when the import did not fail.</param>
/// <param name="HasIssuedSecrets">Whether credentials the import generated are waiting to be claimed once.</param>
/// <param name="CreatedAt">When the package was accepted.</param>
/// <param name="FinishedAt">When the import ended, or <c>null</c>.</param>
public sealed record AccountImportDto(
    Guid Id,
    string State,
    string StateName,
    string AccountName,
    string PrimaryDomain,
    string SourceHostName,
    string KeyFingerprint,
    long SizeBytes,
    long HomeBytes,
    int DatabaseCount,
    IReadOnlyList<string> SectionModules,
    Guid? TargetAccountId,
    string? Stage,
    string? FailureCode,
    string? FailureName,
    bool HasIssuedSecrets,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt);
