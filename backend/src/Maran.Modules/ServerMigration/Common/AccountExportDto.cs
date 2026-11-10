namespace Maran.Modules.ServerMigration.Common;

/// <summary>An account exported for another server, as the administrator sees it.</summary>
/// <param name="Id">The export's id.</param>
/// <param name="AccountName">The account's system user name.</param>
/// <param name="SizeBytes">The artifact's size.</param>
/// <param name="Sha256">The artifact's lowercase hexadecimal SHA-256.</param>
/// <param name="DatabaseCount">How many database dumps the artifact holds.</param>
/// <param name="SectionModules">The ids of the modules that contributed a section.</param>
/// <param name="KeyFingerprint">This server's key fingerprint, which the receiving administrator confirms.</param>
/// <param name="CreatedAt">When the export was made.</param>
/// <param name="ExpiresAt">When its package stops being served.</param>
public sealed record AccountExportDto(
    Guid Id,
    string AccountName,
    long SizeBytes,
    string Sha256,
    int DatabaseCount,
    IReadOnlyList<string> SectionModules,
    string KeyFingerprint,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
