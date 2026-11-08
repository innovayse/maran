using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Common;
using Maran.Modules.Files.Domain.Policies;
using Maran.Modules.Files.Mappers;
using Maran.Modules.Files.Models;
using Maran.Modules.Files.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Files.Commands.RenameFileEntry;

/// <summary>Handles <see cref="RenameFileEntryCommand"/>: admits the account and renames the entry through the agent.</summary>
/// <remarks>
/// A rename is a move to the same folder under another name, so the agent's single rename that never
/// replaces is what refuses a name already taken. The destination is built here from the entry's own
/// folder and the validated single-segment name, which is what keeps a rename from leaving its folder.
/// </remarks>
public sealed class RenameFileEntryCommandHandler
{
    /// <summary>Admits the account: tenant scope, then the suspended-account rule.</summary>
    private readonly FilesAccountGate _gate;

    /// <summary>The agent, which owns the files — this module's only store.</summary>
    private readonly IAgentFilesClient _agent;

    /// <summary>Reads back the renamed entry.</summary>
    private readonly FilesEntryReader _reader;

    /// <summary>This module's audit journal.</summary>
    private readonly FilesAuditJournal _journal;

    /// <summary>Where an agent refusal leaves its code and the operation, and nothing else.</summary>
    private readonly ILogger<RenameFileEntryCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="gate">Admits the account.</param>
    /// <param name="agent">The agent client that renames.</param>
    /// <param name="reader">Reads back the renamed entry.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and operation only.</param>
    public RenameFileEntryCommandHandler(
        FilesAccountGate gate,
        IAgentFilesClient agent,
        FilesEntryReader reader,
        FilesAuditJournal journal,
        ILogger<RenameFileEntryCommandHandler> logger)
    {
        _gate = gate;
        _agent = agent;
        _reader = reader;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Renames the entry and answers it as a row.</summary>
    /// <param name="command">The validated command; see <see cref="RenameFileEntryCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The renamed entry — or <c>AccountNotFound</c>, <c>FilesAccountSuspended</c>, <c>FilesEntryNotFound</c>,
    /// <c>FilesEntryExists</c>, or the module's translation of another agent refusal.
    /// </returns>
    public async Task<Result<FileEntryDto>> HandleAsync(
        RenameFileEntryCommand command,
        CancellationToken cancellationToken)
    {
        var origin = new FilesCallOrigin(command.IpAddress, command.UserAgent);
        var admitted = await _gate.AdmitAsync(command.AccountId, FileAuditActions.FileRenamed, origin, cancellationToken);
        if (!admitted.IsSuccess)
        {
            return Result<FileEntryDto>.Fail(admitted.Error!);
        }

        var account = admitted.Value;
        var destination = FileEntryMapper.PathOf(FilesPathSegmentsPolicy.ParentOf(command.Path), command.NewName);
        var subject = FilesAuditJournal.SubjectOf(account.Username, command.Path + " -> " + destination);

        var moved = await _agent.MoveEntryAsync(account.Username, command.Path, destination, cancellationToken);
        if (!moved.IsSuccess)
        {
            await _journal.RecordFailureAsync(FileAuditActions.FileRenamed, account.Id, subject, origin, CancellationToken.None);
            return Result<FileEntryDto>.Fail(
                FilesAgentErrorTranslator.Translate(
                    _logger, moved.Error!, nameof(_agent.MoveEntryAsync), command.AccountId.ToString()));
        }

        await _journal.RecordSuccessAsync(FileAuditActions.FileRenamed, account.Id, subject, origin, CancellationToken.None);

        return await _reader.FindAsync(account.Username, account.Id, destination, cancellationToken);
    }
}
