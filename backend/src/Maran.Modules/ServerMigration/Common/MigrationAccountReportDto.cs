namespace Maran.Modules.ServerMigration.Common;

/// <summary>The comparison report of one account's cut-over.</summary>
/// <param name="SourceAccountName">The account.</param>
/// <param name="SourceBytes">The size of the source home measured by the final pass.</param>
/// <param name="Slices">How many rsync runs the final pass took.</param>
/// <param name="FilesConverged">Whether the final pass ended successfully.</param>
/// <param name="Databases">The databases loaded.</param>
/// <param name="Sites">What the web server answered for each site.</param>
/// <param name="CutOverAt">When the cut-over finished.</param>
public sealed record MigrationAccountReportDto(
    string SourceAccountName,
    long? SourceBytes,
    int Slices,
    bool FilesConverged,
    IReadOnlyList<MigrationReportDatabaseDto> Databases,
    IReadOnlyList<MigrationReportSiteDto> Sites,
    DateTimeOffset? CutOverAt);
