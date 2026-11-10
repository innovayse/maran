using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Extensions;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.ServerMigration.Commands.RemoveRemoteServer;

/// <summary>Removes a registered remote server and everything stored about it.</summary>
/// <remarks>
/// <para>
/// <b>The panel cannot undo what the administrator did on the server.</b> The public key was installed there by
/// hand and stays until the administrator removes it, so the answer hands back that key and the pinned fingerprint:
/// the interface tells the administrator which line to delete from <c>authorized_keys</c>. Only the PUBLIC key is
/// returned; the private key is deleted with the row and never travels.
/// </para>
/// <para>
/// The rows a discovery left are removed in the same save as the server, so nothing outlives the registration it
/// describes. Nothing is asked of the agent: the removal does not touch the server.
/// </para>
/// </remarks>
public sealed class RemoveRemoteServerCommandHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>This module's audit journal.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="journal">This module's audit journal.</param>
    public RemoveRemoteServerCommandHandler(ServerMigrationDbContext dbContext, ServerMigrationAuditJournal journal)
    {
        _dbContext = dbContext;
        _journal = journal;
    }

    /// <summary>Removes the server, or says that none is registered under that identifier.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The public key still installed on the server and the fingerprint that was pinned; or <c>RemoteServerNotFound</c>.</returns>
    public async Task<Result<RemovedRemoteServerDto>> HandleAsync(
        RemoveRemoteServerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var server = await _dbContext.FindServerAsync(command.ServerId, cancellationToken);
        if (server is null)
        {
            await _journal.RecordFailureAsync(
                MigrationAuditActions.RemoteServerRemoved,
                RemoteServerAuditSubject.OfMissing(command.ServerId),
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<RemovedRemoteServerDto>.Fail(
                Error.Of(nameof(ErrorMessages.RemoteServerNotFound), ErrorType.NotFound));
        }

        // A server a migration job reads from is not removed: its key is what the job's transfers connect with.
        if (await _dbContext.MigrationJobs.AsNoTracking().AnyAsync(job => job.RemoteServerId == server.Id, cancellationToken))
        {
            return Result<RemovedRemoteServerDto>.Fail(
                Error.Of(nameof(ErrorMessages.RemoteServerInUse), ErrorType.Conflict));
        }

        var removed = new RemovedRemoteServerDto(server.PublicKey, server.HostKeyFingerprint);

        await _dbContext.DiscardDiscoveryAsync(server.Id, cancellationToken);
        _dbContext.RemoteServers.Remove(server);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            MigrationAuditActions.RemoteServerRemoved,
            RemoteServerAuditSubject.Of(server),
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<RemovedRemoteServerDto>.Ok(removed);
    }
}
