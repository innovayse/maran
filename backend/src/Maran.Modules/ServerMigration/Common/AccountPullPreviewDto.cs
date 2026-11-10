namespace Maran.Modules.ServerMigration.Common;

/// <summary>
/// What the head of another panel's package says, after its signatures and the confirmed fingerprint verified
/// (issue #127): enough for the administrator to see the pull reaches the right export before the grant's single
/// read of the artifact is spent.
/// </summary>
/// <param name="AccountName">The exported account's system user name.</param>
/// <param name="SourceHostName">The name the exporting server gives itself.</param>
/// <param name="KeyFingerprint">The fingerprint of the key that signed, equal to the one typed.</param>
/// <param name="HomeBytes">The size of the account's home directory.</param>
/// <param name="ArtifactBytes">The size of the artifact a pull would transfer.</param>
/// <param name="DatabaseCount">How many database dumps the artifact holds.</param>
/// <param name="CreatedAt">When the package was made.</param>
public sealed record AccountPullPreviewDto(
    string AccountName,
    string SourceHostName,
    string KeyFingerprint,
    long HomeBytes,
    long ArtifactBytes,
    int DatabaseCount,
    DateTimeOffset CreatedAt);
