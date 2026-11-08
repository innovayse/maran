using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Common;
using Maran.Modules.Files.Domain.Enums;
using Maran.Modules.Files.Mappers;
using Maran.Modules.Files.Models;
using Maran.Modules.Files.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Files.Commands.CreateFileEntry;

/// <summary>Handles <see cref="CreateFileEntryCommand"/>: admits the account and creates the entry through the agent.</summary>
/// <remarks>
/// <para>
/// The account is admitted through <see cref="FilesAccountGate"/> first, so another tenant's account
/// answers 404 and a suspended account answers the module's refusal before the agent is asked
/// anything. The agent is addressed by the system user name the directory returned.
/// </para>
/// <para>
/// A name that is already taken is a conflict: the agent creates a file only when nothing is there
/// and a folder only when nothing holds its final name, so nothing is ever replaced.
/// </para>
/// </remarks>
public sealed class CreateFileEntryCommandHandler
{
    /// <summary>The permission bits of a created file: owner read and write, everyone else read (0644).</summary>
    private const uint FileMode = 0b110_100_100;

    /// <summary>The permission bits of a created folder: owner all, everyone else read and traverse (0755).</summary>
    private const uint DirectoryMode = 0b111_101_101;

    /// <summary>Admits the account: tenant scope, then the suspended-account rule.</summary>
    private readonly FilesAccountGate _gate;

    /// <summary>The agent, which owns the files — this module's only store.</summary>
    private readonly IAgentFilesClient _agent;

    /// <summary>Reads back the entry that was created.</summary>
    private readonly FilesEntryReader _reader;

    /// <summary>This module's audit journal.</summary>
    private readonly FilesAuditJournal _journal;

    /// <summary>Where an agent refusal leaves its code and the operation, and nothing else.</summary>
    private readonly ILogger<CreateFileEntryCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="gate">Admits the account.</param>
    /// <param name="agent">The agent client that creates.</param>
    /// <param name="reader">Reads back the created entry.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and operation only.</param>
    public CreateFileEntryCommandHandler(
        FilesAccountGate gate,
        IAgentFilesClient agent,
        FilesEntryReader reader,
        FilesAuditJournal journal,
        ILogger<CreateFileEntryCommandHandler> logger)
    {
        _gate = gate;
        _agent = agent;
        _reader = reader;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Creates the entry and answers it as a row.</summary>
    /// <param name="command">The validated command; see <see cref="CreateFileEntryCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The created entry — or <c>AccountNotFound</c>, <c>FilesAccountSuspended</c>, <c>FilesEntryExists</c>,
    /// <c>FilesQuotaExceeded</c>, or the module's translation of another agent refusal.
    /// </returns>
    public async Task<Result<FileEntryDto>> HandleAsync(
        CreateFileEntryCommand command,
        CancellationToken cancellationToken)
    {
        var origin = new FilesCallOrigin(command.IpAddress, command.UserAgent);
        var admitted = await _gate.AdmitAsync(command.AccountId, FileAuditActions.FileCreated, origin, cancellationToken);
        if (!admitted.IsSuccess)
        {
            return Result<FileEntryDto>.Fail(admitted.Error!);
        }

        var account = admitted.Value;
        var subject = FilesAuditJournal.SubjectOf(account.Username, command.Path);

        var created = command.Kind == NewFileEntryKind.Directory
            ? await _agent.CreateDirectoryAsync(account.Username, command.Path, DirectoryMode, cancellationToken)
            : await _agent.CreateFileAsync(account.Username, command.Path, FileMode, cancellationToken);
        if (!created.IsSuccess)
        {
            await _journal.RecordFailureAsync(FileAuditActions.FileCreated, account.Id, subject, origin, CancellationToken.None);
            return Result<FileEntryDto>.Fail(
                FilesAgentErrorTranslator.Translate(
                    _logger, created.Error!, nameof(_agent.CreateFileAsync), command.AccountId.ToString()));
        }

        // The entry exists from here on, so the journal does not take the request's token.
        await _journal.RecordSuccessAsync(FileAuditActions.FileCreated, account.Id, subject, origin, CancellationToken.None);

        return await _reader.FindAsync(account.Username, account.Id, command.Path, cancellationToken);
    }
}
