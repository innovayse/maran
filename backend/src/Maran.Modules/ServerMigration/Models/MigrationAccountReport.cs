namespace Maran.Modules.ServerMigration.Models;

/// <summary>
/// The comparison report of one account's cut-over (issue #197, phase 2): what the final pass established, and
/// nothing it did not.
/// </summary>
/// <remarks>
/// <b>Stated limit.</b> <see cref="FilesConverged"/> says the final <c>rsync</c> ended successfully, which is the
/// tool's own guarantee that the destination equals the source at that moment (files that vanished while it ran are
/// the one tolerated difference). The panel holds no second measurement of the home on this host, so the report
/// carries the source's size and not a byte-for-byte comparison, and says so on the screen.
/// </remarks>
/// <param name="SourceBytes">The size of the source home measured by the final pass, or <c>null</c>.</param>
/// <param name="Slices">How many rsync runs the final pass took.</param>
/// <param name="FilesConverged">Whether the final pass ended successfully.</param>
/// <param name="Databases">The databases loaded.</param>
/// <param name="Sites">What the web server answered for each site.</param>
public sealed record MigrationAccountReport(
    long? SourceBytes,
    int Slices,
    bool FilesConverged,
    IReadOnlyList<MigrationDatabaseReport> Databases,
    IReadOnlyList<MigrationSiteReport> Sites);
