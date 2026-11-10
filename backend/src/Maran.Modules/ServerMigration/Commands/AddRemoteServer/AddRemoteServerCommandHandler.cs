using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Extensions;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Commands.AddRemoteServer;

/// <summary>Registers a remote server: makes its key, reads its host keys, and stores it unconfirmed.</summary>
/// <remarks>
/// <para>
/// <b>The order is chosen so that a refusal costs nothing.</b> The name and the endpoint are checked against the
/// registry first, so an administrator who repeats a registration is told so without a key being made. The key is
/// made next and the host keys are read after, and only when both have succeeded is anything written: a server that
/// cannot be reached leaves no half-registered row, and no private key, behind.
/// </para>
/// <para>
/// <b>The private key leaves the agent once, here, and is stored encrypted.</b> It is handed straight to the entity,
/// whose column is encrypted at rest, and it appears in no response, no log line and no audit entry: the entry
/// names the server's address and identifier only. The response carries the PUBLIC key and the line to install.
/// </para>
/// <para>
/// <b>The new server is never trusted.</b> It starts with no pinned host key; the keys it offered wait as
/// candidates until the administrator confirms one by its fingerprint.
/// </para>
/// </remarks>
public sealed class AddRemoteServerCommandHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The agent's connection to remote servers.</summary>
    private readonly IAgentTransferClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>The clock the registration is dated by.</summary>
    private readonly IClock _clock;

    /// <summary>The module's display names, which name the server's state in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="agent">The agent's connection to remote servers.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The clock the registration is dated by.</param>
    /// <param name="names">The module's display names.</param>
    public AddRemoteServerCommandHandler(
        ServerMigrationDbContext dbContext,
        IAgentTransferClient agent,
        ServerMigrationAuditJournal journal,
        IClock clock,
        IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _agent = agent;
        _journal = journal;
        _clock = clock;
        _names = names;
    }

    /// <summary>Registers the server, or says why it cannot be.</summary>
    /// <param name="command">The validated request; see <see cref="AddRemoteServerCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The new server in full; <c>RemoteServerNameTaken</c> or <c>RemoteServerEndpointTaken</c> when the registry
    /// already has it; or the translated reason the agent could not make a key or read the host keys
    /// (<see cref="MigrationAgentErrorTranslator"/>).
    /// </returns>
    public async Task<Result<RemoteServerDetailDto>> HandleAsync(
        AddRemoteServerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = command.Name.Trim();
        var host = SourceHostPolicy.Normalize(command.Host);
        var sshUser = SourceUserPolicy.Normalize(command.SshUser);
        var allowedFrom = AllowedFromPolicy.Require(command.AllowedFrom);
        var attempted = RemoteServerAuditSubject.OfAttempt(host, command.Port);

        var taken = await _dbContext.RemoteServers.FindConflictAsync(
            Guid.Empty, name, host, command.Port, sshUser, cancellationToken);
        if (taken is not null)
        {
            return await RefuseAsync(command, attempted, taken, cancellationToken);
        }

        var generated = await _agent.GenerateSourceKeyAsync(cancellationToken);
        if (!generated.IsSuccess)
        {
            return await RefuseAsync(
                command,
                attempted,
                MigrationAgentErrorTranslator.Translate(generated.Error!, generated.Reason),
                cancellationToken);
        }

        var scanned = await _agent.ScanHostKeyAsync(
            new AgentSourceEndpoint(host, command.Port, sshUser), cancellationToken);
        if (!scanned.IsSuccess)
        {
            return await RefuseAsync(
                command,
                attempted,
                MigrationAgentErrorTranslator.Translate(scanned.Error!, scanned.Reason),
                cancellationToken);
        }

        var server = new RemoteServer(
            Guid.NewGuid(),
            name,
            host,
            command.Port,
            sshUser,
            allowedFrom,
            generated.Value.PublicKey.Trim(),
            generated.Value.PrivateKey.Reveal(),
            _clock.UtcNow);
        server.OfferHostKeys(scanned.Value.Select(key =>
        {
            return (key.Algorithm, key.Key, key.Fingerprint);
        }));
        _dbContext.RemoteServers.Add(server);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two registrations of one name or endpoint that both passed the check above: the unique index
            // refused the second. Ask the same question again; an exception that is not this race is not ours to hide.
            _dbContext.ChangeTracker.Clear();
            var raced = await _dbContext.RemoteServers.FindConflictAsync(
                Guid.Empty, name, host, command.Port, sshUser, cancellationToken);
            if (raced is null)
            {
                throw;
            }

            return await RefuseAsync(command, attempted, raced, cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            MigrationAuditActions.RemoteServerAdded,
            RemoteServerAuditSubject.Of(server),
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<RemoteServerDetailDto>.Ok(RemoteServerMapper.ToDetail(server, [], _names));
    }

    /// <summary>Journals a refused registration and answers with the reason.</summary>
    /// <param name="command">The request, for the caller's address and agent.</param>
    /// <param name="subject">What was attempted.</param>
    /// <param name="error">The reason.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result<RemoteServerDetailDto>> RefuseAsync(
        AddRemoteServerCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            MigrationAuditActions.RemoteServerAdded,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<RemoteServerDetailDto>.Fail(error);
    }
}
