using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Interfaces;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;

namespace Maran.Modules.ServerMigration.Commands.PullExportHead;

/// <summary>
/// Handles <see cref="PullExportHeadCommand"/>: counts the read against the grant and writes the head of the
/// package (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// The read is counted BEFORE anything is written and by the ledger, under a row lock, so a grant read more than
/// <see cref="Domain.Entities.ExportGrant.MaxManifestReads"/> times, or after its artifact was taken, is refused
/// however the requests interleave. Every refusal is the same not-found, whatever the condition: nothing about
/// another tenant's export is confirmed to a caller who holds a grant for a different one.
/// </para>
/// <para>
/// The head is a bounded, signed document and holds no customer data beyond what the sections already name, so
/// previewing costs the exporting server a signature and an agent call and the grant a counted read.
/// </para>
/// </remarks>
public sealed class PullExportHeadCommandHandler
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
    public PullExportHeadCommandHandler(
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

    /// <summary>Serves the head.</summary>
    /// <param name="command">The grant and where to write the head.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success when the head was written; otherwise the one generic refusal, or the writer's failure.</returns>
    public async Task<Result> HandleAsync(PullExportHeadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var grant = await _dbContext.ExportGrants.AsNoTracking().FirstOrDefaultAsync(row => row.Id == command.GrantId, cancellationToken);

        if (grant is null)
        {
            return Refused();
        }

        if (!await _ledger.CountManifestReadAsync(grant.Id, cancellationToken))
        {
            await _journal.RecordPullAsync(grant.Id, grant.ExportId.ToString(), command.IpAddress, command.UserAgent, false, cancellationToken);

            return Refused();
        }

        var export = await _dbContext.AccountExports.AsNoTracking().FirstOrDefaultAsync(row => row.Id == grant.ExportId, cancellationToken);

        if (export is null || !export.IsAvailable(_clock.UtcNow))
        {
            await _journal.RecordPullAsync(grant.Id, grant.ExportId.ToString(), command.IpAddress, command.UserAgent, false, cancellationToken);

            return Refused();
        }

        var written = await _writer.WriteAsync(export, command.Output, PackageExtent.HeadOnly, cancellationToken);

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
