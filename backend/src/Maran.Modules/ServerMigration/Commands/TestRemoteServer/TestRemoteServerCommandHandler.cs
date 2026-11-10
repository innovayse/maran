using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Extensions;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Commands.TestRemoteServer;

/// <summary>Tries a registered server with its pinned host key and records what the probes found, or why it refused.</summary>
/// <remarks>
/// <para>
/// <b>A refusal that describes the SERVER is recorded; one that describes the AGENT is not.</b> A changed host key,
/// a refused login and a server that did not answer are facts about the remote machine, so they are written to its
/// row — a changed host key moves it to <c>HostKeyChanged</c> and nothing connects until the administrator decides —
/// and answered as the matching conflict or outage. An agent that cannot be reached, or fails without saying why,
/// says nothing about the server, so the row is left as it was and the answer is the agent's outage.
/// </para>
/// <para>
/// A server with no pinned host key is refused before the agent is asked: there is nothing to connect with.
/// </para>
/// </remarks>
public sealed class TestRemoteServerCommandHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The agent's connection to remote servers.</summary>
    private readonly IAgentTransferClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>The clock the test is dated by.</summary>
    private readonly IClock _clock;

    /// <summary>The module's display names, which name the server's state in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="agent">The agent's connection to remote servers.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The clock the test is dated by.</param>
    /// <param name="names">The module's display names.</param>
    public TestRemoteServerCommandHandler(
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

    /// <summary>Runs the test and records its outcome.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The server in full, now <c>Ready</c>; <c>RemoteServerNotFound</c> for an identifier that names none;
    /// <c>RemoteServerHostKeyUnconfirmed</c> when no key is pinned; or the translated reason the server refused
    /// (<see cref="MigrationAgentErrorTranslator"/>).
    /// </returns>
    public async Task<Result<RemoteServerDetailDto>> HandleAsync(
        TestRemoteServerCommand command,
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

        if (!server.IsHostKeyPinned)
        {
            return await RefuseAsync(
                command,
                RemoteServerAuditSubject.Of(server),
                Error.Of(nameof(ErrorMessages.RemoteServerHostKeyUnconfirmed), ErrorType.Conflict),
                cancellationToken);
        }

        var tested = await _agent.TestSourceAsync(
            RemoteServerAgentMapper.EndpointOf(server),
            RemoteServerAgentMapper.CredentialOf(server),
            cancellationToken);

        var recorded = tested.IsSuccess
            ? server.RecordTestSucceeded(TestedSourceMapper.ToSnapshot(tested.Value), _clock.UtcNow)
            : RecordRefusal(server, tested.Reason);
        if (!recorded.IsSuccess)
        {
            return await RefuseAsync(command, RemoteServerAuditSubject.Of(server), recorded.Error!, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!tested.IsSuccess)
        {
            return await RefuseAsync(
                command,
                RemoteServerAuditSubject.Of(server),
                MigrationAgentErrorTranslator.Translate(tested.Error!, tested.Reason),
                cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            MigrationAuditActions.RemoteServerTested,
            RemoteServerAuditSubject.Of(server),
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        var warnings = await _dbContext.ReadWarningsAsync(server.Id, _names, cancellationToken);

        return Result<RemoteServerDetailDto>.Ok(RemoteServerMapper.ToDetail(server, warnings, _names));
    }

    /// <summary>Writes a refusal that describes the server on its row; any other failure leaves the row alone.</summary>
    /// <param name="server">The server that was tried.</param>
    /// <param name="reason">Why the server refused, when the agent stated it.</param>
    /// <returns>Success; the row was changed only when the reason describes the server.</returns>
    private Result RecordRefusal(RemoteServer server, SourceFailureReason reason)
    {
        return MigrationAgentErrorTranslator.RecordedFailureOf(reason) is { } failure
            ? server.RecordTestFailed(failure, _clock.UtcNow)
            : Result.Ok();
    }

    /// <summary>Journals a refused test and answers with the reason.</summary>
    /// <param name="command">The request, for the caller's address and agent.</param>
    /// <param name="subject">What the test was attempted on.</param>
    /// <param name="error">The reason.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result<RemoteServerDetailDto>> RefuseAsync(
        TestRemoteServerCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            MigrationAuditActions.RemoteServerTested,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<RemoteServerDetailDto>.Fail(error);
    }
}
