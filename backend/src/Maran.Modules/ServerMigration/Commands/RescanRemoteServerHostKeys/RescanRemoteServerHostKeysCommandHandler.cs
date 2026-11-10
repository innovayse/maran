using Maran.Agent.Client.Interfaces;
using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Extensions;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Commands.RescanRemoteServerHostKeys;

/// <summary>Reads the host keys a registered server offers now and offers them to the administrator again.</summary>
/// <remarks>
/// <para>
/// <b>A rescan never pins.</b> It replaces the candidates and leaves the pinned key exactly as it was: a server that
/// rotated its key shows the new one beside the old, and only the administrator's confirmation, by fingerprint,
/// trusts it. The scan is made to the address the server is registered under and asks nothing of it but its public
/// keys, so it needs no credential.
/// </para>
/// <para>
/// A server the agent cannot reach, or whose address the agent refuses, changes nothing: the candidates the
/// administrator was looking at stay.
/// </para>
/// </remarks>
public sealed class RescanRemoteServerHostKeysCommandHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The agent's connection to remote servers.</summary>
    private readonly IAgentTransferClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>The module's display names, which name the server's state in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="agent">The agent's connection to remote servers.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="names">The module's display names.</param>
    public RescanRemoteServerHostKeysCommandHandler(
        ServerMigrationDbContext dbContext,
        IAgentTransferClient agent,
        ServerMigrationAuditJournal journal,
        IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _agent = agent;
        _journal = journal;
        _names = names;
    }

    /// <summary>Replaces the candidates with what the server offers now, or says why it could not be asked.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The server in full; <c>RemoteServerNotFound</c> for an identifier that names none; or the translated reason
    /// the keys could not be read (<see cref="MigrationAgentErrorTranslator"/>).
    /// </returns>
    public async Task<Result<RemoteServerDetailDto>> HandleAsync(
        RescanRemoteServerHostKeysCommand command,
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

        var scanned = await _agent.ScanHostKeyAsync(RemoteServerAgentMapper.EndpointOf(server), cancellationToken);
        if (!scanned.IsSuccess)
        {
            return await RefuseAsync(
                command,
                RemoteServerAuditSubject.Of(server),
                MigrationAgentErrorTranslator.Translate(scanned.Error!, scanned.Reason),
                cancellationToken);
        }

        server.OfferHostKeys(scanned.Value.Select(key =>
        {
            return (key.Algorithm, key.Key, key.Fingerprint);
        }));
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            MigrationAuditActions.RemoteServerHostKeysScanned,
            RemoteServerAuditSubject.Of(server),
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        var warnings = await _dbContext.ReadWarningsAsync(server.Id, _names, cancellationToken);

        return Result<RemoteServerDetailDto>.Ok(RemoteServerMapper.ToDetail(server, warnings, _names));
    }

    /// <summary>Journals a refused rescan and answers with the reason.</summary>
    /// <param name="command">The request, for the caller's address and agent.</param>
    /// <param name="subject">What the rescan was attempted on.</param>
    /// <param name="error">The reason.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result<RemoteServerDetailDto>> RefuseAsync(
        RescanRemoteServerHostKeysCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            MigrationAuditActions.RemoteServerHostKeysScanned,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<RemoteServerDetailDto>.Fail(error);
    }
}
