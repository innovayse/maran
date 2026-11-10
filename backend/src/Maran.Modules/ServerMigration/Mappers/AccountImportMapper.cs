using System.Text.Json;
using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Resources;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Mappers;

/// <summary>Restates an import and an export on the wire. It translates; it never decides.</summary>
public static class AccountImportMapper
{
    /// <summary>Projects an import.</summary>
    /// <param name="import">The import.</param>
    /// <param name="failureName">The sentence for the import's failure code in the caller's language, decided by the caller; <c>null</c> when it did not fail.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    /// <returns>The wire shape.</returns>
    public static AccountImportDto ToDto(
        AccountImport import,
        string? failureName,
        IStringLocalizer<DisplayNames> names)
    {
        return new AccountImportDto(
            import.Id,
            import.State.ToString(),
            MigrationEnumNames.Of(names, import.State),
            import.SourceAccountName,
            import.SourcePrimaryDomain,
            import.SourceHostName,
            import.KeyFingerprint,
            import.ArtifactSizeBytes,
            import.HomeBytes,
            import.DatabaseCount,
            ModulesOf(import.SectionsJson),
            import.TargetAccountId,
            import.Stage,
            import.FailureCode,
            failureName,
            import.IssuedSecretsJson is not null,
            import.CreatedAt,
            import.FinishedAt);
    }

    /// <summary>Projects an export.</summary>
    /// <param name="export">The export.</param>
    /// <param name="keyFingerprint">This server's key fingerprint.</param>
    /// <returns>The wire shape.</returns>
    public static AccountExportDto ToDto(AccountExport export, string keyFingerprint)
    {
        return new AccountExportDto(
            export.Id,
            export.SourceAccountName,
            export.SizeBytes,
            export.Sha256,
            export.DatabaseCount,
            ModulesOf(export.SectionsJson),
            keyFingerprint,
            export.CreatedAt,
            export.ExpiresAt);
    }

    /// <summary>Names the modules whose sections a stored array holds.</summary>
    /// <param name="sectionsJson">The stored array.</param>
    /// <returns>The module ids, in package order; empty when the text is not an array of sections.</returns>
    private static List<string> ModulesOf(string sectionsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<MigrationSection>>(sectionsJson, JsonSerializerOptions.Web)?
                .Select(section =>
                {
                    return section.ModuleId;
                })
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
