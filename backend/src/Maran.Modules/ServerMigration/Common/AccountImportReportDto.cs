namespace Maran.Modules.ServerMigration.Common;

/// <summary>The dry run of an import: every finding of every module, and whether the import may start.</summary>
/// <remarks>
/// Nothing about any account, site, database, cron entry or login has changed to produce it. The start button's
/// state is advice: the backend refuses to start an import with a conflict whatever the screen says.
/// </remarks>
/// <param name="ImportId">The import judged.</param>
/// <param name="CanStart">Whether the report holds no conflict.</param>
/// <param name="Findings">The findings, conflicts first.</param>
public sealed record AccountImportReportDto(
    Guid ImportId,
    bool CanStart,
    IReadOnlyList<MigrationFindingDto> Findings);
