using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.ServerMigration.Commands.DownloadAccountExport;

/// <summary>
/// Handles <see cref="DownloadAccountExportCommand"/>: checks the export is still available and streams its
/// package (issue #127).
/// </summary>
/// <remarks>
/// A package can be downloaded by an administrator any number of times until the export expires. Pulling by
/// another panel is the single-use path and has its own grant. A failure after the first byte truncates the
/// stream, which the receiver and its agent refuse (see <see cref="MigrationPackageWriter"/>).
/// </remarks>
public sealed class DownloadAccountExportCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>Writes the package.</summary>
    private readonly MigrationPackageWriter _writer;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Records the download.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="writer">Writes the package.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">Records the download.</param>
    public DownloadAccountExportCommandHandler(
        ServerMigrationDbContext dbContext,
        MigrationPackageWriter writer,
        IClock clock,
        ServerMigrationAuditJournal journal)
    {
        _dbContext = dbContext;
        _writer = writer;
        _clock = clock;
        _journal = journal;
    }

    /// <summary>Streams the package.</summary>
    /// <param name="command">The export and where to write it.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>Success when the whole package was written, otherwise the failure.</returns>
    public async Task<Result> HandleAsync(DownloadAccountExportCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var export = await _dbContext.AccountExports.AsNoTracking().FirstOrDefaultAsync(row => row.Id == command.ExportId, cancellationToken);

        if (export is null || !export.IsAvailable(_clock.UtcNow))
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.MigrationExportNotFound), ErrorType.NotFound));
        }

        var written = await _writer.WriteAsync(export, command.Output, PackageExtent.Whole, cancellationToken);

        if (written.IsSuccess)
        {
            await _journal.RecordSuccessAsync(
                MigrationAuditActions.ExportDownloaded, export.SourceAccountName, command.IpAddress, command.UserAgent, cancellationToken);
        }
        else
        {
            await _journal.RecordFailureAsync(
                MigrationAuditActions.ExportDownloaded, export.SourceAccountName, command.IpAddress, command.UserAgent, cancellationToken);
        }

        return written;
    }
}
