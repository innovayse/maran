using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Extensions;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Commands.EditRemoteServer;

/// <summary>Changes a registered remote server's label or where the panel connects to it.</summary>
/// <remarks>
/// The registry's rules are the registration's: a name is unique and so is a login on a host and port. The entity
/// decides what a change of endpoint withdraws, so a server pointed at another machine can never keep the host key
/// that was confirmed for the first one, and what a discovery of the old target found goes with it. Nothing here
/// talks to the agent: the new endpoint is tried when the
/// administrator tests it, after confirming its host key.
/// </remarks>
public sealed class EditRemoteServerCommandHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>This module's audit journal.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>The module's display names, which name the server's state in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="names">The module's display names.</param>
    public EditRemoteServerCommandHandler(
        ServerMigrationDbContext dbContext,
        ServerMigrationAuditJournal journal,
        IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _journal = journal;
        _names = names;
    }

    /// <summary>Applies the change, or says why it cannot be applied.</summary>
    /// <param name="command">The validated request; see <see cref="EditRemoteServerCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The server in full; <c>RemoteServerNotFound</c> for an identifier that names none; or
    /// <c>RemoteServerNameTaken</c> / <c>RemoteServerEndpointTaken</c> when another server already has it.
    /// </returns>
    public async Task<Result<RemoteServerDetailDto>> HandleAsync(
        EditRemoteServerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var server = await _dbContext.FindServerAsync(command.ServerId, cancellationToken);
        if (server is null)
        {
            return await RefuseAsync(
                command,
                RemoteServerAuditSubject.OfMissing(command.ServerId),
                Error.Of(nameof(ErrorMessages.RemoteServerNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var name = command.Name.Trim();
        var host = SourceHostPolicy.Normalize(command.Host);
        var sshUser = SourceUserPolicy.Normalize(command.SshUser);
        var allowedFrom = AllowedFromPolicy.Require(command.AllowedFrom);

        var taken = await _dbContext.RemoteServers.FindConflictAsync(
            server.Id, name, host, command.Port, sshUser, cancellationToken);
        if (taken is not null)
        {
            return await RefuseAsync(command, RemoteServerAuditSubject.Of(server), taken, cancellationToken);
        }

        if (server.Retarget(name, host, command.Port, sshUser, allowedFrom))
        {
            // The rows a discovery of the old target left describe another machine; they go with the pin.
            await _dbContext.DiscardDiscoveryAsync(server.Id, cancellationToken);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another change took the name or endpoint between the check and the write; the unique index refused
            // this one. Ask the same question again; an exception that is not this race is not ours to hide.
            _dbContext.ChangeTracker.Clear();
            var raced = await _dbContext.RemoteServers.FindConflictAsync(
                server.Id, name, host, command.Port, sshUser, cancellationToken);
            if (raced is null)
            {
                throw;
            }

            return await RefuseAsync(command, RemoteServerAuditSubject.Of(server), raced, cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            MigrationAuditActions.RemoteServerEdited,
            RemoteServerAuditSubject.Of(server),
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        var warnings = await _dbContext.ReadWarningsAsync(server.Id, _names, cancellationToken);

        return Result<RemoteServerDetailDto>.Ok(RemoteServerMapper.ToDetail(server, warnings, _names));
    }

    /// <summary>Journals a refused change and answers with the reason.</summary>
    /// <param name="command">The request, for the caller's address and agent.</param>
    /// <param name="subject">What the change was attempted on.</param>
    /// <param name="error">The reason.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result<RemoteServerDetailDto>> RefuseAsync(
        EditRemoteServerCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            MigrationAuditActions.RemoteServerEdited,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<RemoteServerDetailDto>.Fail(error);
    }
}
