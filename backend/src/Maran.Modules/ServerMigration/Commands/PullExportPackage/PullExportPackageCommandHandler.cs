using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Interfaces;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;

namespace Maran.Modules.ServerMigration.Commands.PullExportPackage;

/// <summary>
/// Handles <see cref="PullExportPackageCommand"/>: takes the grant's single read of the artifact and streams the
/// package (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// <b>The read is taken BEFORE the first byte is written, and kept even if the stream then fails.</b> A grant half
/// used is a grant a thief may have half used, so an interrupted pull needs a new grant; that is the price of the
/// guarantee that one grant is one copy of the data. The ledger takes it under a row lock, so of any number of
/// simultaneous requests with one grant exactly one is served.
/// </para>
/// <para>
/// A failure after the first byte truncates the stream. The receiver reads exactly the signed number of artifact
/// bytes and its agent refuses a short stream, so a truncated package is a refused one (see
/// <see cref="MigrationPackageWriter"/>).
/// </para>
/// </remarks>
public sealed class PullExportPackageCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>Spends the grant's reads.</summary>
    private readonly IExportGrantLedger _ledger;

    /// <summary>Writes the package.</summary>
    private readonly MigrationPackageWriter _writer;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Records the pull.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="ledger">Spends the grant's reads.</param>
    /// <param name="writer">Writes the package.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">Records the pull.</param>
    public PullExportPackageCommandHandler(
        ServerMigrationDbContext dbContext,
        IExportGrantLedger ledger,
        MigrationPackageWriter writer,
        IClock clock,
        ServerMigrationAuditJournal journal)
    {
        _dbContext = dbContext;
        _ledger = ledger;
        _writer = writer;
        _clock = clock;
        _journal = journal;
    }

    /// <summary>Serves the package.</summary>
    /// <param name="command">The grant and where to write the package.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success when the whole package was written; otherwise the one generic refusal, or the writer's failure.</returns>
    public async Task<Result> HandleAsync(PullExportPackageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var grant = await _dbContext.ExportGrants.AsNoTracking().FirstOrDefaultAsync(row => row.Id == command.GrantId, cancellationToken);

        if (grant is null)
        {
            return Refused();
        }

        var export = await _dbContext.AccountExports.AsNoTracking().FirstOrDefaultAsync(row => row.Id == grant.ExportId, cancellationToken);

        // The export is judged BEFORE the read is taken: an expired export must not burn the grant's one read, and
        // the read taken for a package that can be served is the only one spent.
        if (export is null || !export.IsAvailable(_clock.UtcNow) || !await _ledger.TakeArtifactReadAsync(grant.Id, cancellationToken))
        {
            await _journal.RecordPullAsync(grant.Id, grant.ExportId.ToString(), command.IpAddress, command.UserAgent, false, cancellationToken);

            return Refused();
        }

        var written = await _writer.WriteAsync(export, command.Output, PackageExtent.Whole, cancellationToken);

        await _journal.RecordPullAsync(
            grant.Id, export.SourceAccountName, command.IpAddress, command.UserAgent, written.IsSuccess, cancellationToken);

        return written;
    }

    /// <summary>The one refusal a pull gets, whatever the condition.</summary>
    /// <returns>A failed result carrying the generic not-found.</returns>
    private static Result Refused()
    {
        return Result.Fail(Error.Of(nameof(ErrorMessages.MigrationExportNotFound), ErrorType.NotFound));
    }
}
