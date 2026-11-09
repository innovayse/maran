using System.Text;
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
using AgentFileEntryKind = Maran.Agent.Client.Services.FilesService.FileEntryKind;

namespace Maran.Modules.Files.Commands.SaveFileText;

/// <summary>Handles <see cref="SaveFileTextCommand"/>: checks the file has not changed and writes the new text through the agent.</summary>
/// <remarks>
/// <para>
/// The write is the agent's atomic one (a temporary file beside the target, then a rename), so a
/// failed save leaves the old file and never half of the new one. The mode of an existing file is read
/// first and passed to the write, and a new file gets <see cref="FilesModePolicy.DefaultFileMode"/>.
/// </para>
/// <para>
/// The text is held in memory and bounded by <c>FilesOptions.MaxEditBytes</c>, measured in UTF-8
/// bytes, before anything is sent. The agent bounds its own write independently of that figure.
/// </para>
/// </remarks>
public sealed class SaveFileTextCommandHandler
{
    /// <summary>Admits the account: tenant scope, then the suspended-account rule.</summary>
    private readonly FilesAccountGate _gate;

    /// <summary>Finds the entry at the path.</summary>
    private readonly FilesEntryLookup _lookup;

    /// <summary>The agent, which owns the files — this module's only store.</summary>
    private readonly IAgentFilesClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly FilesAuditJournal _journal;

    /// <summary>The limits, read at each call so a reloaded setting takes effect.</summary>
    private readonly IOptionsMonitor<FilesOptions> _options;

    /// <summary>Where an agent refusal leaves its code and the operation, and nothing else.</summary>
    private readonly ILogger<SaveFileTextCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="gate">Admits the account.</param>
    /// <param name="lookup">Finds the entry at the path.</param>
    /// <param name="agent">The agent client that writes the file.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="options">The limits.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and operation only.</param>
    public SaveFileTextCommandHandler(
        FilesAccountGate gate,
        FilesEntryLookup lookup,
        IAgentFilesClient agent,
        FilesAuditJournal journal,
        IOptionsMonitor<FilesOptions> options,
        ILogger<SaveFileTextCommandHandler> logger)
    {
        _gate = gate;
        _lookup = lookup;
        _agent = agent;
        _journal = journal;
        _options = options;
        _logger = logger;
    }

    /// <summary>Saves the text.</summary>
    /// <param name="command">The validated parameters; see <see cref="SaveFileTextCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The new size and modification time — or <c>AccountNotFound</c>, <c>FilesAccountSuspended</c>,
    /// <c>FilesNotEditable</c>, <c>FilesEditConflict</c>, <c>FilesQuotaExceeded</c>, or the module's
    /// translation of another agent refusal.
    /// </returns>
    public async Task<Result<FileSavedDto>> HandleAsync(SaveFileTextCommand command, CancellationToken cancellationToken)
    {
        var origin = new FilesCallOrigin(command.IpAddress, command.UserAgent);

        var admitted = await _gate.AdmitAsync(command.AccountId, FileAuditActions.FileSaved, origin, cancellationToken);
        if (!admitted.IsSuccess)
        {
            return Result<FileSavedDto>.Fail(admitted.Error!);
        }

        var account = admitted.Value;
        var subject = FilesAuditJournal.SubjectOf(account.Username, command.Path);

        if (Encoding.UTF8.GetByteCount(command.Content) > _options.CurrentValue.MaxEditBytes)
        {
            return await FailAsync(
                account.Id, subject, origin, Error.Of(nameof(ErrorMessages.FilesNotEditable), ErrorType.Validation));
        }

        var existing = await _lookup.FindAsync(account.Username, account.Id, command.Path, cancellationToken);
        var mode = FilesModePolicy.DefaultFileMode;
        if (existing.IsSuccess)
        {
            if (existing.Value.Kind != AgentFileEntryKind.File)
            {
                return await FailAsync(
                    account.Id, subject, origin, Error.Of(nameof(ErrorMessages.FilesNotEditable), ErrorType.Validation));
            }

            if (command.ExpectedModifiedAt is { } expected && existing.Value.ModifiedAt != expected)
            {
                return await FailAsync(
                    account.Id, subject, origin, Error.Of(nameof(ErrorMessages.FilesEditConflict), ErrorType.Conflict));
            }

            mode = existing.Value.Mode & FilesModePolicy.MaximumMode;
        }
        else if (existing.Error!.Code != nameof(ErrorMessages.FilesEntryNotFound))
        {
            return await FailAsync(account.Id, subject, origin, existing.Error);
        }
        else if (command.ExpectedModifiedAt is not null)
        {
            // The editor loaded a file that has since been removed: writing would resurrect it unasked.
            return await FailAsync(
                account.Id, subject, origin, Error.Of(nameof(ErrorMessages.FilesEditConflict), ErrorType.Conflict));
        }

        var written = await _agent.WriteFileAsync(account.Username, command.Path, command.Content, mode, cancellationToken);
        if (!written.IsSuccess)
        {
            return await FailAsync(
                account.Id,
                subject,
                origin,
                FilesAgentErrorTranslator.Translate(
                    _logger, written.Error!, nameof(_agent.WriteFileAsync), account.Id.ToString()));
        }

        await _journal.RecordSuccessAsync(FileAuditActions.FileSaved, account.Id, subject, origin, CancellationToken.None);

        var saved = await _lookup.FindAsync(account.Username, account.Id, command.Path, cancellationToken);
        return saved.IsSuccess
            ? Result<FileSavedDto>.Ok(new FileSavedDto(written.Value, saved.Value.ModifiedAt))
            : Result<FileSavedDto>.Fail(saved.Error!);
    }

    /// <summary>Journals a refused save and returns it as the typed failure.</summary>
    /// <param name="accountId">The account whose file was being saved.</param>
    /// <param name="subject">What the save was attempted on.</param>
    /// <param name="origin">The caller's address and user agent.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<FileSavedDto>> FailAsync(
        Guid accountId,
        string subject,
        FilesCallOrigin origin,
        Error error)
    {
        await _journal.RecordFailureAsync(FileAuditActions.FileSaved, accountId, subject, origin, CancellationToken.None);
        return Result<FileSavedDto>.Fail(error);
    }
}
