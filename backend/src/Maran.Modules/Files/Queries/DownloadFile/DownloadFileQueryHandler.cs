using Maran.Agent.Client.Interfaces;
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
using AgentOpenedFileRead = Maran.Agent.Client.Services.FilesService.OpenedFileRead;

namespace Maran.Modules.Files.Queries.DownloadFile;

/// <summary>Handles <see cref="DownloadFileQuery"/>: checks the file and opens the agent's stream, and streams nothing yet.</summary>
/// <remarks>
/// <para>
/// Everything that can be refused is refused HERE, before the first byte and before any header is set:
/// the account, the transfer slot, a path that is not a regular file, and a file larger than
/// <c>FilesOptions.MaxDownloadBytes</c>. A refusal is therefore a clean JSON problem document and never
/// a truncated body. The agent bounds the stream itself as well, so a file that grows past the cap while
/// it is read ends the transfer instead of being sent whole.
/// </para>
/// <para>
/// The returned <see cref="FileDownload"/> owns the transfer slot and the agent stream. Its copy refuses to
/// finish quietly when the agent ends the stream with a different number of bytes than the listing
/// promised, because the response has already declared that length and a body of another length is a
/// download the browser would otherwise save as if it were whole. The completion is journalled by the
/// controller through <c>CompleteFileDownloadCommand</c> once the last byte is written; a transfer that
/// dies leaves the opening entry alone, which is the honest record.
/// </para>
/// </remarks>
public sealed class DownloadFileQueryHandler
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
    private readonly ILogger<DownloadFileQueryHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="gate">Admits the account.</param>
    /// <param name="lookup">Finds the entry at the path.</param>
    /// <param name="agent">The agent client that streams the file.</param>
    /// <param name="slots">The transfer bounds.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="options">The limits.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and operation only.</param>
    public DownloadFileQueryHandler(
        FilesAccountGate gate,
        FilesEntryLookup lookup,
        IAgentFilesClient agent,
        FilesTransferSlots slots,
        FilesAuditJournal journal,
        IOptionsMonitor<FilesOptions> options,
        ILogger<DownloadFileQueryHandler> logger)
    {
        _gate = gate;
        _lookup = lookup;
        _agent = agent;
        _slots = slots;
        _journal = journal;
        _options = options;
        _logger = logger;
    }

    /// <summary>Opens the file for streaming.</summary>
    /// <param name="query">The validated query; see <see cref="DownloadFileQueryValidator"/>.</param>
    /// <param name="cancellationToken">Cancels opening the file.</param>
    /// <returns>
    /// The download — or <c>AccountNotFound</c>, <c>FilesAccountSuspended</c>, <c>FilesAccountBusy</c>,
    /// <c>FilesServerBusy</c>, <c>FilesEntryNotFound</c> (also for a directory), <c>FilesDownloadTooLarge</c>,
    /// or the module's translation of another agent refusal.
    /// </returns>
    public async Task<Result<FileDownload>> HandleAsync(DownloadFileQuery query, CancellationToken cancellationToken)
    {
        var origin = new FilesCallOrigin(query.IpAddress, query.UserAgent);

        var admitted = await _gate.AdmitAsync(
            query.AccountId, FileAuditActions.FileDownloadStarted, origin, cancellationToken);
        if (!admitted.IsSuccess)
        {
            return Result<FileDownload>.Fail(admitted.Error!);
        }

        var account = admitted.Value;
        var subject = FilesAuditJournal.SubjectOf(account.Username, query.Path);

        var slot = _slots.TryAcquire(account.Id);
        if (!slot.IsSuccess)
        {
            return await RefuseAsync(account.Id, subject, origin, slot.Error!);
        }

        var lease = slot.Value;
        AgentOpenedFileRead? stream = null;
        var handedOver = false;
        try
        {
            var maxDownloadBytes = _options.CurrentValue.MaxDownloadBytes;

            var found = await _lookup.FindAsync(account.Username, account.Id, query.Path, cancellationToken);
            if (!found.IsSuccess)
            {
                return await RefuseAsync(account.Id, subject, origin, found.Error!);
            }

            var entry = found.Value;
            if (entry.Kind != AgentFileEntryKind.File)
            {
                return await RefuseAsync(
                    account.Id,
                    subject,
                    origin,
                    Error.Of(nameof(ErrorMessages.FilesEntryNotFound), ErrorType.NotFound));
            }

            if (entry.SizeBytes > (ulong)maxDownloadBytes)
            {
                return await RefuseAsync(
                    account.Id,
                    subject,
                    origin,
                    Error.Of(nameof(ErrorMessages.FilesDownloadTooLarge), ErrorType.Validation));
            }

            var opened = await _agent.ReadFileAsync(
                account.Username, query.Path, (ulong)maxDownloadBytes, cancellationToken);
            if (!opened.IsSuccess)
            {
                return await RefuseAsync(
                    account.Id,
                    subject,
                    origin,
                    FilesAgentErrorTranslator.Translate(
                        _logger, opened.Error!, nameof(_agent.ReadFileAsync), account.Id.ToString()));
            }

            var owned = opened.Value;
            stream = owned;
            await _journal.RecordSuccessAsync(
                FileAuditActions.FileDownloadStarted, account.Id, subject, origin, CancellationToken.None);

            var download = new FileDownload(
                FilesDownloadNamePolicy.Sanitise(entry.Name),
                entry.SizeBytes,
                async (destination, token) =>
                {
                    await CopyExactlyAsync(owned, entry.SizeBytes, destination, token);
                },
                async () =>
                {
                    await owned.DisposeAsync();
                    lease.Dispose();
                });

            // From here the download owns the stream and the slot; the finally below must not give them back.
            handedOver = true;
            return Result<FileDownload>.Ok(download);
        }
        finally
        {
            if (!handedOver)
            {
                if (stream is not null)
                {
                    await stream.DisposeAsync();
                }

                lease.Dispose();
            }
        }
    }

    /// <summary>Copies the stream to the destination and requires it to be exactly the length the response promised.</summary>
    /// <param name="stream">The agent's stream.</param>
    /// <param name="expectedBytes">The size the listing reported, and the response declared.</param>
    /// <param name="destination">The response body.</param>
    /// <param name="cancellationToken">Cancelled when the client goes away.</param>
    /// <returns>A task that ends when the last byte is written.</returns>
    /// <exception cref="IOException">
    /// The agent ended the stream with an error, or with a different number of bytes than were declared:
    /// the transfer cannot be completed and the connection must be closed rather than finished.
    /// </exception>
    private static async Task CopyExactlyAsync(
        AgentOpenedFileRead stream,
        ulong expectedBytes,
        Stream destination,
        CancellationToken cancellationToken)
    {
        // The agent client has already logged the agent's refusal, by code, at the boundary that received it.
        var copied = await stream.CopyToAsync(destination, cancellationToken);
        if (!copied.IsSuccess)
        {
            throw new IOException("The agent ended the file stream before the file was complete.");
        }

        if (copied.Value != expectedBytes)
        {
            throw new IOException("The agent streamed a different number of bytes than the file was listed with.");
        }
    }

    /// <summary>Journals a refused download and returns it as the typed failure.</summary>
    /// <param name="accountId">The account whose file was asked for.</param>
    /// <param name="subject">What the download was attempted on.</param>
    /// <param name="origin">The caller's address and user agent.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<FileDownload>> RefuseAsync(
        Guid accountId,
        string subject,
        FilesCallOrigin origin,
        Error error)
    {
        await _journal.RecordFailureAsync(
            FileAuditActions.FileDownloadStarted, accountId, subject, origin, CancellationToken.None);
        return Result<FileDownload>.Fail(error);
    }
}
