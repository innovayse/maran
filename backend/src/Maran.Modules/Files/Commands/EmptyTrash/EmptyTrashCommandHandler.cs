using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Common;
using Maran.Modules.Files.Mappers;
using Maran.Modules.Files.Models;
using Maran.Modules.Files.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Files.Commands.EmptyTrash;

/// <summary>Handles <see cref="EmptyTrashCommand"/>: admits the account and removes everything in its trash through the agent.</summary>
/// <remarks>
/// An empty trash is a success with a count of zero. The audit subject is the account alone: the entries
/// were the customer's own, already journalled when they were trashed, and their paths are text read from
/// a place the customer can edit.
/// </remarks>
public sealed class EmptyTrashCommandHandler
{
    /// <summary>Admits the account: tenant scope, then the suspended-account rule.</summary>
    private readonly FilesAccountGate _gate;

    /// <summary>The agent, which owns the files and the trash beside them.</summary>
    private readonly IAgentFilesClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly FilesAuditJournal _journal;

    /// <summary>Where an agent refusal leaves its code and the operation, and nothing else.</summary>
    private readonly ILogger<EmptyTrashCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="gate">Admits the account.</param>
    /// <param name="agent">The agent client that empties the trash.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and operation only.</param>
    public EmptyTrashCommandHandler(
        FilesAccountGate gate,
        IAgentFilesClient agent,
        FilesAuditJournal journal,
        ILogger<EmptyTrashCommandHandler> logger)
    {
        _gate = gate;
        _agent = agent;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Empties the trash and answers how many entries went.</summary>
    /// <param name="command">The validated command; see <see cref="EmptyTrashCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The count removed — or <c>AccountNotFound</c>, <c>FilesAccountSuspended</c>, <c>FilesServerBusy</c>
    /// or the module's translation of another agent refusal.
    /// </returns>
    public async Task<Result<EmptiedTrashDto>> HandleAsync(EmptyTrashCommand command, CancellationToken cancellationToken)
    {
        var origin = new FilesCallOrigin(command.IpAddress, command.UserAgent);
        var admitted = await _gate.AdmitAsync(
            command.AccountId, FileAuditActions.FileTrashEmptied, origin, cancellationToken);
        if (!admitted.IsSuccess)
        {
            return Result<EmptiedTrashDto>.Fail(admitted.Error!);
        }

        var account = admitted.Value;
        var subject = FilesAuditJournal.SubjectOf(account.Username, string.Empty);

        var emptied = await _agent.EmptyTrashAsync(account.Username, cancellationToken);
        if (!emptied.IsSuccess)
        {
            await _journal.RecordFailureAsync(
                FileAuditActions.FileTrashEmptied, account.Id, subject, origin, CancellationToken.None);
            return Result<EmptiedTrashDto>.Fail(
                FilesAgentErrorTranslator.Translate(
                    _logger, emptied.Error!, nameof(_agent.EmptyTrashAsync), command.AccountId.ToString()));
        }

        await _journal.RecordSuccessAsync(
            FileAuditActions.FileTrashEmptied, account.Id, subject, origin, CancellationToken.None);

        return Result<EmptiedTrashDto>.Ok(new EmptiedTrashDto((int)Math.Min(emptied.Value, int.MaxValue)));
    }
}
