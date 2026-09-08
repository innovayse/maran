using Maran.Modules.Backups.Domain.Policies;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Models;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Resources;

namespace Maran.Modules.Backups.Services;

/// <summary>
/// Turns "which destination" into the one answer an operation may act on: a recorded row's identity
/// and the destination an agent call carries.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every path that acts on a destination comes through here, and that is the point.</b> Create,
/// restore, delete and retention each used to build a destination out of the module's options at
/// their own call site, which meant four places that would each have to be told about a stored
/// destination and four places a remote one could reach the agent through. One resolver is one place
/// to ask <see cref="RemoteDestinationPolicy"/>, and one place a test can aim a refusal at.
/// </para>
/// <para>
/// <b><c>null</c> means the default destination, which is what a historic backup row's null already
/// meant.</b> Rows written before the destination table existed carry no destination id and were
/// documented as meaning "the configured local root"; that root is now a row, so those rows resolve
/// to it and keep the meaning they were written with. Nothing is backfilled and the column is not
/// narrowed — see this task's report.
/// </para>
/// <para>
/// <b>A missing default is a refusal, not a fallback to the options.</b> A panel whose destination
/// row is absent is a panel whose startup reconciliation did not run, and quietly rebuilding the
/// destination from configuration would mean backups written against a destination the panel has no
/// record of — invisible to retention, and stamping a null onto rows that were supposed to stop
/// carrying one. It answers a server-side failure, because nothing the caller sent is wrong.
/// </para>
/// </remarks>
public sealed class BackupDestinationResolver
{
    /// <summary>The Backups module's database context.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>Creates the resolver.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    public BackupDestinationResolver(BackupsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Resolves the destination an operation will use.</summary>
    /// <param name="destinationId">The destination named, or <c>null</c> for the default one.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The identity and the agent-facing shape, or a named refusal: the destination does not exist,
    /// this panel has no default destination, or the destination is of a kind this build cannot act
    /// on.
    /// </returns>
    /// <remarks>
    /// The remote refusal is the SAME code and the same kind the save endpoint answers with, not a
    /// second one meaning the same thing. One condition with two codes is two strings a screen has to
    /// branch on and two resx entries that can drift into saying different things about one state.
    /// </remarks>
    public async Task<Result<ResolvedBackupDestination>> ResolveAsync(
        Guid? destinationId,
        CancellationToken cancellationToken)
    {
        var destination = destinationId is null
            ? await _dbContext.BackupDestinations
                .FirstOrDefaultAsync(row => row.IsDefault, cancellationToken)
            : await _dbContext.BackupDestinations
                .FirstOrDefaultAsync(row => row.Id == destinationId.Value, cancellationToken);

        if (destination is null)
        {
            return destinationId is null
                ? Result<ResolvedBackupDestination>.Fail(
                    Error.Of(nameof(ErrorMessages.BackupDestinationNotConfigured), ErrorType.Failure))
                : Result<ResolvedBackupDestination>.Fail(
                    Error.Of(nameof(ErrorMessages.BackupDestinationNotFound), ErrorType.NotFound));
        }

        if (!RemoteDestinationPolicy.Admits(destination.Kind))
        {
            return Result<ResolvedBackupDestination>.Fail(
                Error.Of(nameof(ErrorMessages.BackupDestinationRemoteUnsupported), ErrorType.Validation));
        }

        return Result<ResolvedBackupDestination>.Ok(
            new ResolvedBackupDestination(destination.Id, BackupDestinationMapper.ForAgent(destination)));
    }
}
