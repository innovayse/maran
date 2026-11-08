using System.Text;
using System.Text.Unicode;
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

namespace Maran.Modules.Files.Queries.ReadFileText;

/// <summary>Handles <see cref="ReadFileTextQuery"/>: reads a small text file through the agent and returns it as a string.</summary>
/// <remarks>
/// <para>
/// A file is editable when it is a regular file, no larger than <c>FilesOptions.MaxEditBytes</c>, holds no
/// NUL byte and is valid UTF-8. Anything else answers <c>FilesNotEditable</c>. The size is checked from the
/// listing BEFORE the file is opened, so a large file costs one directory page and not a read, and is
/// bounded again by the agent when it streams (the file may have grown in between).
/// </para>
/// <para>
/// The modification time returned is the one the listing reported before the read. It is what the save
/// compares against, and it is not atomic with the read: a write landing between the two leaves a
/// time older than the content, which makes the next save report a conflict rather than overwrite it.
/// </para>
/// </remarks>
public sealed class ReadFileTextQueryHandler
{
    /// <summary>Admits the account: tenant scope, then the suspended-account rule.</summary>
    private readonly FilesAccountGate _gate;

    /// <summary>Finds the entry at the path.</summary>
    private readonly FilesEntryLookup _lookup;

    /// <summary>The agent, which owns the files — this module's only store.</summary>
    private readonly IAgentFilesClient _agent;

    /// <summary>The limits, read at each call so a reloaded setting takes effect.</summary>
    private readonly IOptionsMonitor<FilesOptions> _options;

    /// <summary>Where an agent refusal leaves its code and the operation, and nothing else.</summary>
    private readonly ILogger<ReadFileTextQueryHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="gate">Admits the account.</param>
    /// <param name="lookup">Finds the entry at the path.</param>
    /// <param name="agent">The agent client that streams the file.</param>
    /// <param name="options">The limits.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and operation only.</param>
    public ReadFileTextQueryHandler(
        FilesAccountGate gate,
        FilesEntryLookup lookup,
        IAgentFilesClient agent,
        IOptionsMonitor<FilesOptions> options,
        ILogger<ReadFileTextQueryHandler> logger)
    {
        _gate = gate;
        _lookup = lookup;
        _agent = agent;
        _options = options;
        _logger = logger;
    }

    /// <summary>Reads the file.</summary>
    /// <param name="query">The validated query; see <see cref="ReadFileTextQueryValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The text — or <c>AccountNotFound</c>, <c>FilesAccountSuspended</c>, <c>FilesEntryNotFound</c>,
    /// <c>FilesNotEditable</c>, or the module's translation of another agent refusal.
    /// </returns>
    public async Task<Result<FileTextDto>> HandleAsync(ReadFileTextQuery query, CancellationToken cancellationToken)
    {
        var admitted = await _gate.AdmitAsync(
            query.AccountId,
            FileAuditActions.FileAccessRefused,
            new FilesCallOrigin(query.IpAddress, query.UserAgent),
            cancellationToken);
        if (!admitted.IsSuccess)
        {
            return Result<FileTextDto>.Fail(admitted.Error!);
        }

        var username = admitted.Value.Username;
        var maxEditBytes = _options.CurrentValue.MaxEditBytes;

        var found = await _lookup.FindAsync(username, query.AccountId, query.Path, cancellationToken);
        if (!found.IsSuccess)
        {
            return Result<FileTextDto>.Fail(found.Error!);
        }

        var entry = found.Value;
        if (entry.Kind != AgentFileEntryKind.File || entry.SizeBytes > (ulong)maxEditBytes)
        {
            return NotEditable();
        }

        var opened = await _agent.ReadFileAsync(username, query.Path, (ulong)maxEditBytes, cancellationToken);
        if (!opened.IsSuccess)
        {
            return Result<FileTextDto>.Fail(
                FilesAgentErrorTranslator.Translate(
                    _logger, opened.Error!, nameof(_agent.ReadFileAsync), query.AccountId.ToString()));
        }

        await using var stream = opened.Value;
        using var buffer = new MemoryStream();
        var copied = await stream.CopyToAsync(buffer, cancellationToken);
        if (!copied.IsSuccess)
        {
            return Result<FileTextDto>.Fail(
                FilesAgentErrorTranslator.Translate(
                    _logger, copied.Error!, nameof(stream.CopyToAsync), query.AccountId.ToString()));
        }

        var text = DecodeText(buffer);
        if (text is null)
        {
            return NotEditable();
        }

        return Result<FileTextDto>.Ok(
            new FileTextDto(text, entry.ModifiedAt, FilesModePolicy.Format(entry.Mode), (ulong)buffer.Length));
    }

    /// <summary>Reads the buffer as text the editor can hold.</summary>
    /// <param name="buffer">What the agent streamed.</param>
    /// <returns>The text — or null when it holds a NUL byte or is not valid UTF-8.</returns>
    private static string? DecodeText(MemoryStream buffer)
    {
        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        return bytes.Contains((byte)0) || !Utf8.IsValid(bytes) ? null : Encoding.UTF8.GetString(bytes);
    }

    /// <summary>The refusal for a file that is not text the editor can hold.</summary>
    /// <returns>The failed result.</returns>
    private static Result<FileTextDto> NotEditable()
    {
        return Result<FileTextDto>.Fail(Error.Of(nameof(ErrorMessages.FilesNotEditable), ErrorType.Validation));
    }
}
