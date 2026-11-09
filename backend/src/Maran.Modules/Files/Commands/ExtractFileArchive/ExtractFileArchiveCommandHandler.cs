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

namespace Maran.Modules.Files.Commands.ExtractFileArchive;

/// <summary>Handles <see cref="ExtractFileArchiveCommand"/>: admits the account and unpacks the archive through the agent.</summary>
/// <remarks>
/// <para>
/// The format is read from the file name by <see cref="ArchiveFormatPolicy"/> and the agent verifies the
/// real format, so a name that lies is refused there. The panel does not enforce the extraction bounds:
/// it hands them to the agent from <see cref="FilesOptions"/> and the agent holds them, counting the bytes
/// as they are written and not trusting the archive's own headers. A member the agent will not write
/// (a path leaving the destination, a link, a device) refuses the whole archive, and the customer sees
/// <c>FilesArchiveRefused</c> without the member's name.
/// </para>
/// <para>The work runs inside the request; the agent runs it to completion even when the caller goes away.</para>
/// </remarks>
public sealed class ExtractFileArchiveCommandHandler
{
    /// <summary>Admits the account: tenant scope, then the suspended-account rule.</summary>
    private readonly FilesAccountGate _gate;

    /// <summary>The agent, which owns the files — this module's only store.</summary>
    private readonly IAgentFilesClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly FilesAuditJournal _journal;

    /// <summary>The extraction bounds, read at each run so a reloaded setting takes effect.</summary>
    private readonly IOptionsMonitor<FilesOptions> _options;

    /// <summary>Where an agent refusal leaves its code and the operation, and nothing else.</summary>
    private readonly ILogger<ExtractFileArchiveCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="gate">Admits the account.</param>
    /// <param name="agent">The agent client that unpacks.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="options">The module's limits.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and operation only.</param>
    public ExtractFileArchiveCommandHandler(
        FilesAccountGate gate,
        IAgentFilesClient agent,
        FilesAuditJournal journal,
        IOptionsMonitor<FilesOptions> options,
        ILogger<ExtractFileArchiveCommandHandler> logger)
    {
        _gate = gate;
        _agent = agent;
        _journal = journal;
        _options = options;
        _logger = logger;
    }

    /// <summary>Unpacks the archive and answers how many entries were written.</summary>
    /// <param name="command">The validated command; see <see cref="ExtractFileArchiveCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the wait; the agent finishes what it started.</param>
    /// <returns>
    /// The entry count — or <c>AccountNotFound</c>, <c>FilesAccountSuspended</c>, <c>FilesArchiveFormatUnknown</c>,
    /// <c>FilesEntryNotFound</c>, <c>FilesQuotaExceeded</c>, <c>FilesArchiveRefused</c>, or the module's
    /// translation of another agent refusal.
    /// </returns>
    public async Task<Result<FileExtractedDto>> HandleAsync(
        ExtractFileArchiveCommand command,
        CancellationToken cancellationToken)
    {
        var origin = new FilesCallOrigin(command.IpAddress, command.UserAgent);
        var admitted = await _gate.AdmitAsync(command.AccountId, FileAuditActions.FileExtracted, origin, cancellationToken);
        if (!admitted.IsSuccess)
        {
            return Result<FileExtractedDto>.Fail(admitted.Error!);
        }

        var account = admitted.Value;
        var subject = FilesAuditJournal.SubjectOf(account.Username, command.ArchivePath + " -> " + command.DestinationDirectory);

        // The validator refused a name the module does not recognise; this is the second look a command
        // dispatched on the bus without it deserves.
        if (!ArchiveFormatPolicy.TryFromName(FilesPathSegmentsPolicy.NameOf(command.ArchivePath), out var format))
        {
            await _journal.RecordFailureAsync(FileAuditActions.FileExtracted, account.Id, subject, origin, CancellationToken.None);
            return Result<FileExtractedDto>.Fail(Error.Of(nameof(ErrorMessages.FilesArchiveFormatUnknown), ErrorType.Validation));
        }

        var limits = _options.CurrentValue;
        var extracted = await _agent.ExtractArchiveAsync(
            account.Username,
            command.ArchivePath,
            command.DestinationDirectory,
            ArchiveFormatMapper.ToAgent(format),
            (ulong)limits.ExtractMaxEntries,
            (ulong)limits.ExtractMaxBytes,
            (uint)limits.ExtractMaxRatio,
            cancellationToken);
        if (!extracted.IsSuccess)
        {
            await _journal.RecordFailureAsync(FileAuditActions.FileExtracted, account.Id, subject, origin, CancellationToken.None);
            return Result<FileExtractedDto>.Fail(
                FilesAgentErrorTranslator.Translate(
                    _logger, extracted.Error!, nameof(_agent.ExtractArchiveAsync), command.AccountId.ToString()));
        }

        await _journal.RecordSuccessAsync(FileAuditActions.FileExtracted, account.Id, subject, origin, CancellationToken.None);

        return Result<FileExtractedDto>.Ok(new FileExtractedDto((int)Math.Min(extracted.Value, int.MaxValue)));
    }
}
