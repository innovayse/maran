using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Common;
using Maran.Modules.Files.Domain.Policies;
using Maran.Modules.Files.Mappers;
using Maran.Modules.Files.Models;
using Maran.Modules.Files.Options;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Files.Commands.CreateFileArchive;

/// <summary>Handles <see cref="CreateFileArchiveCommand"/>: admits the account and packs the entries through the agent.</summary>
/// <remarks>
/// <para>
/// The archive is one operation over the whole list, not a batch of independent items: the sources go to
/// the agent together and one archive comes back or none does. The list is still capped at
/// <see cref="FilesOptions.MaxBatchItems"/> before the agent is called, so one request cannot name
/// an unbounded number of sources.
/// </para>
/// <para>
/// The agent holds the size bounds (<see cref="FilesTreeBoundsPolicy"/>): the tree is measured first, and
/// one over the bound is refused as <c>FilesArchiveRefused</c> before anything is written. The work runs
/// inside the request; the agent runs it to completion even when the caller goes away.
/// </para>
/// </remarks>
public sealed class CreateFileArchiveCommandHandler
{
    /// <summary>Admits the account: tenant scope, then the suspended-account rule.</summary>
    private readonly FilesAccountGate _gate;

    /// <summary>The agent, which owns the files — this module's only store.</summary>
    private readonly IAgentFilesClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly FilesAuditJournal _journal;

    /// <summary>The batch ceiling, read at each run so a reloaded setting takes effect.</summary>
    private readonly IOptionsMonitor<FilesOptions> _options;

    /// <summary>Where an agent refusal leaves its code and the operation, and nothing else.</summary>
    private readonly ILogger<CreateFileArchiveCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="gate">Admits the account.</param>
    /// <param name="agent">The agent client that packs.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="options">The module's limits.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and operation only.</param>
    public CreateFileArchiveCommandHandler(
        FilesAccountGate gate,
        IAgentFilesClient agent,
        FilesAuditJournal journal,
        IOptionsMonitor<FilesOptions> options,
        ILogger<CreateFileArchiveCommandHandler> logger)
    {
        _gate = gate;
        _agent = agent;
        _journal = journal;
        _options = options;
        _logger = logger;
    }

    /// <summary>Packs the entries and answers the archive's size.</summary>
    /// <param name="command">The validated command; see <see cref="CreateFileArchiveCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the wait; the agent finishes what it started.</param>
    /// <returns>
    /// The archive's size — or <c>AccountNotFound</c>, <c>FilesAccountSuspended</c>, <c>FilesBatchTooLarge</c>,
    /// <c>FilesEntryExists</c>, <c>FilesQuotaExceeded</c>, <c>FilesArchiveRefused</c>, or the module's
    /// translation of another agent refusal.
    /// </returns>
    public async Task<Result<FileArchivedDto>> HandleAsync(
        CreateFileArchiveCommand command,
        CancellationToken cancellationToken)
    {
        var origin = new FilesCallOrigin(command.IpAddress, command.UserAgent);
        var admitted = await _gate.AdmitAsync(command.AccountId, FileAuditActions.FileArchived, origin, cancellationToken);
        if (!admitted.IsSuccess)
        {
            return Result<FileArchivedDto>.Fail(admitted.Error!);
        }

        var account = admitted.Value;
        var subject = FilesAuditJournal.SubjectOf(account.Username, command.DestinationPath);

        if (command.SourcePaths.Count > _options.CurrentValue.MaxBatchItems)
        {
            await _journal.RecordFailureAsync(FileAuditActions.FileArchived, account.Id, subject, origin, CancellationToken.None);
            return Result<FileArchivedDto>.Fail(Error.Of(nameof(ErrorMessages.FilesBatchTooLarge), ErrorType.Validation));
        }

        var archived = await _agent.CreateArchiveAsync(
            account.Username,
            command.SourcePaths,
            command.DestinationPath,
            ArchiveFormatMapper.ToAgent(command.Format),
            FilesTreeBoundsPolicy.MaximumEntries,
            FilesTreeBoundsPolicy.MaximumBytes,
            cancellationToken);
        if (!archived.IsSuccess)
        {
            await _journal.RecordFailureAsync(FileAuditActions.FileArchived, account.Id, subject, origin, CancellationToken.None);
            return Result<FileArchivedDto>.Fail(
                FilesAgentErrorTranslator.Translate(
                    _logger, archived.Error!, nameof(_agent.CreateArchiveAsync), command.AccountId.ToString()));
        }

        await _journal.RecordSuccessAsync(FileAuditActions.FileArchived, account.Id, subject, origin, CancellationToken.None);

        return Result<FileArchivedDto>.Ok(new FileArchivedDto(archived.Value));
    }
}
