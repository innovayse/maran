using System.Globalization;
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

namespace Maran.Modules.Files.Commands.UploadFile;

/// <summary>Handles <see cref="UploadFileCommand"/>: forwards the request body to the agent and counts it on the way.</summary>
/// <remarks>
/// <para>
/// <b>What stops an oversized upload, in order.</b> The declared length, when there is one, refuses
/// before the account's transfer slot is taken. Then the agent's client counts the bytes it forwards and
/// ends the transfer one byte past <c>FilesOptions.MaxUploadBytes</c>, so a client that lies about its
/// length, or sends none, is stopped by the count and not by a header. The agent bounds its own write
/// independently of this figure.
/// </para>
/// <para>
/// <b>A cancelled upload leaves no file.</b> The agent writes a temporary file and renames it only when
/// the stream completes; a client that disconnects cancels the call, the agent discards what it had, and
/// the journal records the upload as aborted. The write is never retried: a stream that has been read
/// from cannot be sent again.
/// </para>
/// <para>
/// An existing file keeps its permissions when it is overwritten; a new file is given
/// <see cref="FilesModePolicy.DefaultFileMode"/>. The slot is held for the life of the transfer and given
/// back on every path out of this method.
/// </para>
/// </remarks>
public sealed class UploadFileCommandHandler
{
    /// <summary>Admits the account: tenant scope, then the suspended-account rule.</summary>
    private readonly FilesAccountGate _gate;

    /// <summary>Finds the entry at the path.</summary>
    private readonly FilesEntryLookup _lookup;

    /// <summary>The agent, which owns the files — this module's only store.</summary>
    private readonly IAgentFilesClient _agent;

    /// <summary>The per-account and host-wide transfer bounds.</summary>
    private readonly FilesTransferSlots _slots;

    /// <summary>This module's audit journal.</summary>
    private readonly FilesAuditJournal _journal;

    /// <summary>The limits, read at each call so a reloaded setting takes effect.</summary>
    private readonly IOptionsMonitor<FilesOptions> _options;

    /// <summary>Where an agent refusal leaves its code and the operation, and nothing else.</summary>
    private readonly ILogger<UploadFileCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="gate">Admits the account.</param>
    /// <param name="lookup">Finds the entry at the path.</param>
    /// <param name="agent">The agent client that writes the file.</param>
    /// <param name="slots">The transfer bounds.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="options">The limits.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and operation only.</param>
    public UploadFileCommandHandler(
        FilesAccountGate gate,
        FilesEntryLookup lookup,
        IAgentFilesClient agent,
        FilesTransferSlots slots,
        FilesAuditJournal journal,
        IOptionsMonitor<FilesOptions> options,
        ILogger<UploadFileCommandHandler> logger)
    {
        _gate = gate;
        _lookup = lookup;
        _agent = agent;
        _slots = slots;
        _journal = journal;
        _options = options;
        _logger = logger;
    }

    /// <summary>Writes the body to the file.</summary>
    /// <param name="command">The validated parameters; see <see cref="UploadFileCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancelled when the client goes away.</param>
    /// <returns>
    /// The number of bytes written — or <c>AccountNotFound</c>, <c>FilesAccountSuspended</c>,
    /// <c>FilesAccountBusy</c>, <c>FilesServerBusy</c>, <c>FilesUploadTooLarge</c>, <c>FilesEntryExists</c>,
    /// <c>FilesQuotaExceeded</c>, or the module's translation of another agent refusal.
    /// </returns>
    public async Task<Result<FileUploadedDto>> HandleAsync(
        UploadFileCommand command,
        CancellationToken cancellationToken)
    {
        var origin = new FilesCallOrigin(command.IpAddress, command.UserAgent);

        var admitted = await _gate.AdmitAsync(
            command.AccountId, FileAuditActions.FileUploadAborted, origin, cancellationToken);
        if (!admitted.IsSuccess)
        {
            return Result<FileUploadedDto>.Fail(admitted.Error!);
        }

        var account = admitted.Value;
        var subject = FilesAuditJournal.SubjectOf(account.Username, command.Path);
        var maxUploadBytes = _options.CurrentValue.MaxUploadBytes;

        if (command.Body is null)
        {
            // The action stamps the body; a command without one was not dispatched by it.
            throw new InvalidOperationException("An upload command is dispatched with the request body.");
        }

        if (command.DeclaredBytes > maxUploadBytes)
        {
            return await AbortAsync(
                account.Id, subject, origin, Error.Of(nameof(ErrorMessages.FilesUploadTooLarge), ErrorType.Validation));
        }

        var slot = _slots.TryAcquire(account.Id);
        if (!slot.IsSuccess)
        {
            return await AbortAsync(account.Id, subject, origin, slot.Error!);
        }

        using var lease = slot.Value;

        var mode = await ModeForAsync(account, command, cancellationToken);
        if (!mode.IsSuccess)
        {
            return await AbortAsync(account.Id, subject, origin, mode.Error!);
        }

        // The opening entry names the length the client declared, if it declared one, so an operator can
        // set it beside the count the completion entry records.
        var declared = command.DeclaredBytes is { } length
            ? $"{subject} (declared {length.ToString(CultureInfo.InvariantCulture)} bytes)"
            : subject;
        await _journal.RecordSuccessAsync(
            FileAuditActions.FileUploadStarted, account.Id, declared, origin, CancellationToken.None);

        Result<ulong> written;
        try
        {
            written = await _agent.WriteFileStreamAsync(
                account.Username, command.Path, mode.Value, command.Body, (ulong)maxUploadBytes, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client went away mid-transfer. The agent discards its temporary file; the journal records
            // that the upload did not complete, and the cancellation then travels on unchanged.
            await _journal.RecordFailureAsync(
                FileAuditActions.FileUploadAborted, account.Id, subject, origin, CancellationToken.None);
            throw;
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // The server's own body limit, set one byte past the cap by the action, fired before the agent
            // client could count the byte: the answer is the same readable refusal the count gives.
            return await AbortAsync(
                account.Id, subject, origin, Error.Of(nameof(ErrorMessages.FilesUploadTooLarge), ErrorType.Validation));
        }

        if (!written.IsSuccess)
        {
            return await AbortAsync(
                account.Id,
                subject,
                origin,
                FilesAgentErrorTranslator.Translate(
                    _logger, written.Error!, nameof(_agent.WriteFileStreamAsync), account.Id.ToString()));
        }

        await _journal.RecordSuccessAsync(
            FileAuditActions.FileUploaded,
            account.Id,
            $"{subject} ({written.Value.ToString(CultureInfo.InvariantCulture)} bytes)",
            origin,
            CancellationToken.None);

        return Result<FileUploadedDto>.Ok(new FileUploadedDto(written.Value));
    }

    /// <summary>
    /// Decides the mode the file is written with, and whether the write may go ahead: a file already there
    /// keeps its mode when it may be replaced, and a path that is taken by anything else is refused.
    /// </summary>
    /// <param name="account">The admitted account.</param>
    /// <param name="command">The upload.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The permission bits to write with — or <c>FilesEntryExists</c>, or the lookup's refusal.</returns>
    private async Task<Result<uint>> ModeForAsync(
        AccountSnapshot account,
        UploadFileCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await _lookup.FindAsync(account.Username, account.Id, command.Path, cancellationToken);
        if (!existing.IsSuccess)
        {
            return existing.Error!.Code == nameof(ErrorMessages.FilesEntryNotFound)
                ? Result<uint>.Ok(FilesModePolicy.DefaultFileMode)
                : Result<uint>.Fail(existing.Error!);
        }

        if (!command.Overwrite || existing.Value.Kind != AgentFileEntryKind.File)
        {
            return Result<uint>.Fail(Error.Of(nameof(ErrorMessages.FilesEntryExists), ErrorType.Conflict));
        }

        return Result<uint>.Ok(existing.Value.Mode & FilesModePolicy.MaximumMode);
    }

    /// <summary>Journals an upload that did not happen and returns it as the typed failure.</summary>
    /// <param name="accountId">The account whose file was being written.</param>
    /// <param name="subject">What the upload was attempted on.</param>
    /// <param name="origin">The caller's address and user agent.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<FileUploadedDto>> AbortAsync(
        Guid accountId,
        string subject,
        FilesCallOrigin origin,
        Error error)
    {
        await _journal.RecordFailureAsync(
            FileAuditActions.FileUploadAborted, accountId, subject, origin, CancellationToken.None);
        return Result<FileUploadedDto>.Fail(error);
    }
}
